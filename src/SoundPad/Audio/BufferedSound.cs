using System.IO;
using System.Threading.Channels;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundPad.Audio;

// One bounded producer per playing pad. Only the producer touches the file/decoder.
// The audio callback consumes ready samples without waiting for disk or a decoder.
public sealed class BufferedSound : IDisposable
{
    private const int BlockSamples = 8192;
    private const int QueueBlocks = 16;
    private readonly Channel<float[]> blocks = Channel.CreateBounded<float[]>(new BoundedChannelOptions(QueueBlocks)
    {
        SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
    });
    private readonly CancellationTokenSource cancellation = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private float[]? current;
    private int position;
    private bool ended;
    private int disposed;
    private long underruns, consumedSamples;
    public long ConsumedSamples => Interlocked.Read(ref consumedSamples);
    public TimeSpan TotalDuration { get; private set; }
    public TimeSpan Position
    {
        get
        {
            var elapsed = TimeSpan.FromSeconds(ConsumedSamples / 96000d);
            if (Loop && TotalDuration > TimeSpan.Zero)
                return TimeSpan.FromTicks(elapsed.Ticks % TotalDuration.Ticks);
            return elapsed <= TotalDuration ? elapsed : TotalDuration;
        }
    }
    private Exception? error;
    public bool Loop { get; set; }
    public Exception? Error => Volatile.Read(ref error);
    public long Underruns => Interlocked.Read(ref underruns);
    public Task Completion { get; private set; } = Task.CompletedTask;
    public const int MaximumBufferedBytes = (QueueBlocks + 2) * BlockSamples * sizeof(float);

    private BufferedSound(string path) { Completion = Task.Run(() => Produce(path)); }

    public static async Task<BufferedSound> OpenAsync(string path)
    {
        var sound = new BufferedSound(path);
        try { await sound.ready.Task; return sound; }
        catch { sound.Dispose(); throw; }
    }

    private async Task Produce(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            TotalDuration = reader.TotalTime;
            ISampleProvider CreateSource()
            {
                ISampleProvider source = reader;
                if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
                else if (source.WaveFormat.Channels != 2) throw new InvalidDataException("Bitte eine Mono- oder Stereo-Datei verwenden.");
                if (source.WaveFormat.SampleRate != 48000) source = new WdlResamplingSampleProvider(source, 48000);
                return source;
            }
            var source = CreateSource();
            long cycleSamples = 0;
            var producedBlocks = 0;
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var buffer = new float[BlockSamples];
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    if (cycleSamples == 0) throw new InvalidDataException("Die Datei enthält keine Audiodaten.");
                    // Boundary markers let the consumer decide exactly at EOF whether to loop.
                    await blocks.Writer.WriteAsync(Array.Empty<float>(), cancellation.Token);
                    ready.TrySetResult();
                    reader.Position = 0;
                    source = CreateSource();
                    cycleSamples = 0;
                    continue;
                }
                if (read % 2 != 0) throw new InvalidDataException("Unvollständiger Stereo-Frame.");
                if (read != buffer.Length) Array.Resize(ref buffer, read);
                for (var i = 0; i < read; i++) if (!float.IsFinite(buffer[i])) buffer[i] = 0;
                await blocks.Writer.WriteAsync(buffer, cancellation.Token);
                cycleSamples += read;
                if (++producedBlocks >= 4) ready.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { ready.TrySetCanceled(); }
        catch (Exception failure)
        {
            Volatile.Write(ref error, failure);
            ready.TrySetException(failure);
        }
        finally { blocks.Writer.TryComplete(); }
    }

    // Called only by the mixer while it holds its voice lock. Writes are additive.
    public bool MixInto(float[] output, int offset, int count, float volume)
    {
        if (ended) return false;
        for (var i = 0; i < count;)
        {
            if (current is null || position == current.Length)
            {
                current = null;
                if (!blocks.Reader.TryRead(out var next))
                {
                    if (blocks.Reader.Completion.IsCompleted) { ended = true; return false; }
                    Interlocked.Increment(ref underruns);
                    return true; // Remaining output stays silent; never block the audio thread.
                }
                if (next.Length == 0)
                {
                    if (!Loop) { ended = true; return false; }
                    continue;
                }
                current = next;
                position = 0;
            }
            var take = Math.Min(count - i, current.Length - position);
            for (var j = 0; j < take; j++)
                output[offset + i + j] += current[position + j] * volume;
            Interlocked.Add(ref consumedSamples, take);
            position += take;
            i += take;
        }
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) cancellation.Cancel();
        // Producer finally disposes the decoder. Never wait for slow storage on the UI/audio thread.
    }
}

