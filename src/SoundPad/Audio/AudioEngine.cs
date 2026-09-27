using System.IO;
using System.Collections.Concurrent;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.Wave.SampleProviders;
using SoundPad.Diagnostics;

namespace SoundPad.Audio;

public sealed class AudioEngine : IDisposable
{
    // Bounded offline diagnostic helper; normal playback streams via BufferedSound.
    public const int MaxSamplesPerSound = 16 * 1024 * 1024;

    private readonly ConcurrentQueue<Exception> errors = new();
    private WasapiOut? output;
    private MMDevice? outputDevice;

    private volatile bool disposed;
    public PadMixer Mixer { get; } = new();
    public bool TryTakeError(out Exception? error) => errors.TryDequeue(out error);

    // Called on UI thread, after loading; initialization failures leave it retryable.
    public void EnsureOutput()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (output is not null) return;
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        WasapiOut? candidate = null;
        try
        {
            var deviceName = device.FriendlyName;
            var deviceId = device.ID;
            var mixFormat = device.AudioClient.MixFormat.ToString();
            candidate = new WasapiOut(device, AudioClientShareMode.Shared, true, 80);
            candidate.Init(Mixer.ToWaveProvider());
            var openedOutput = candidate;
            candidate.PlaybackStopped += (_, e) =>
            {
                // Ignore notifications from a previous output that has already been reset.
                if (ReferenceEquals(output, openedOutput) && e.Exception is not null)
                    errors.Enqueue(e.Exception);
            };
            outputDevice = device;
            output = candidate;
            candidate.Play();
            AppLog.Write("Information", "Audio.OutputOpened", new
            {
                backend = "WASAPI", mode = "Shared", deviceName, deviceId,
                deviceMixFormat = mixFormat,
                streamFormat = candidate.OutputWaveFormat.ToString(),
                requestedLatencyMs = 80, sampleRate = 48000, channels = 2
            });
        }
        catch
        {
            output = null;
            outputDevice = null;
            candidate?.Dispose();
            device.Dispose();
            throw;
        }
    }
    public async Task<BufferedSound> PrepareAsync(string path)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var timing = AppLog.Measure("Audio.StreamPrepareDuration");
        var sound = await BufferedSound.OpenAsync(path);
        if (disposed) { sound.Dispose(); throw new ObjectDisposedException(nameof(AudioEngine)); }
        AppLog.Write("Information", "Audio.StreamPrepared", new
        {
            fileName = Path.GetFileName(path), maximumBufferBytes = BufferedSound.MaximumBufferedBytes
        });
        return sound;
    }
    public async Task<TimeSpan> ValidateAsync(string path)
    {
        using var sound = await PrepareAsync(path);
        return sound.TotalDuration;
    }

    public static Task<TimeSpan> GetDurationAsync(string path) => Task.Run(() =>
    {
        using var reader = new AudioFileReader(path);
        return reader.TotalTime;
    });
    public static float[] Decode(string path)
    {
        using var timing = AppLog.Measure("Audio.DecodeDuration");
        using var reader = new AudioFileReader(path);
        ISampleProvider source = reader;
        if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
        else if (source.WaveFormat.Channels != 2) throw new InvalidDataException("Bitte eine Mono- oder Stereo-Datei verwenden.");
        if (source.WaveFormat.SampleRate != 48000) source = new WdlResamplingSampleProvider(source, 48000);
        var data = new List<float>();
        var block = new float[16384];
        int read;
        while ((read = source.Read(block, 0, block.Length)) > 0)
        {
            if (data.Count + read > MaxSamplesPerSound) throw new InvalidDataException("Für die Offline-Diagnose zu groß. Die normale Wiedergabe verwendet Streaming.");
            for (var i = 0; i < read; i++) data.Add(float.IsFinite(block[i]) ? block[i] : 0);
        }
        if (data.Count == 0 || data.Count % 2 != 0) throw new InvalidDataException("Die Datei enthält keine vollständigen Audiodaten.");
        AppLog.Write("Information", "Audio.Decoded", new { fileName = Path.GetFileName(path), samples = data.Count, durationSeconds = data.Count / 96000d });
        return data.ToArray();
    }
    public void ResetOutput()
    {
        Mixer.StopAll();
        var previous = output;
        var device = outputDevice;
        output = null;
        outputDevice = null;
        try { previous?.Dispose(); }
        finally { device?.Dispose(); }
    }
    public void Dispose() { disposed = true; ResetOutput(); }
}



