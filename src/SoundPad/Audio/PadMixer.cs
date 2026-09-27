using NAudio.Wave;

namespace SoundPad.Audio;

// All voices share one sample clock. No file access, logging or UI work in Read.
public sealed class PadMixer : ISampleProvider
{
    private sealed class Voice(float[] samples, bool loop, float volume)
    {
        public readonly float[] Samples = samples;
        public BufferedSound? Stream;
        public int Position;
        public bool Loop = loop;
        public float Volume = volume;
        public bool Playing = true;
    }
    private readonly object gate = new();
    private readonly Dictionary<Guid, Voice> voices = [];
    private float masterVolume = .8f;
    private long clippedSamples;
    private long renderedFrames;
    public long RenderedFrames { get { lock (gate) return renderedFrames; } }
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public float MasterVolume { get { lock (gate) return masterVolume; } set { lock (gate) masterVolume = Math.Clamp(value, 0, 1); } }
    public void Start(Guid id, float[] samples, bool loop, float volume, float? startMasterVolume = null)
    {
        if (samples.Length == 0 || samples.Length % 2 != 0) throw new ArgumentException("Leere oder unvollständige Audiodaten.");
        lock (gate)
        {
            if (startMasterVolume.HasValue) masterVolume = Math.Clamp(startMasterVolume.Value, 0, 1);
            voices[id] = new(samples, loop, Math.Clamp(volume, 0, 1));
        }
    }
    // Takes ownership of the stream; stopping/removing the voice releases its decoder.
    public void Start(Guid id, BufferedSound sound, bool loop, float volume, float? startMasterVolume = null)
    {
        lock (gate)
        {
            if (voices.Remove(id, out var previous)) previous.Stream?.Dispose();
            if (startMasterVolume.HasValue) masterVolume = Math.Clamp(startMasterVolume.Value, 0, 1);
            sound.Loop = loop;
            voices[id] = new([], loop, Math.Clamp(volume, 0, 1)) { Stream = sound };
        }
    }
    public void Update(Guid id, bool loop, float volume)
    {
        lock (gate) if (voices.TryGetValue(id, out var voice))
        {
            voice.Loop = loop; voice.Volume = Math.Clamp(volume, 0, 1);
            if (voice.Stream is not null) voice.Stream.Loop = loop;
        }
    }
    public bool IsPlaying(Guid id) { lock (gate) return voices.TryGetValue(id, out var voice) && voice.Playing; }
    public Exception? GetError(Guid id) { lock (gate) return voices.GetValueOrDefault(id)?.Stream?.Error; }
    public long GetUnderruns(Guid id) { lock (gate) return voices.GetValueOrDefault(id)?.Stream?.Underruns ?? 0; }
    public (TimeSpan Position, TimeSpan Duration)? GetPlaybackTime(Guid id)
    {
        lock (gate)
        {
            var stream = voices.GetValueOrDefault(id)?.Stream;
            return stream is null ? null : (stream.Position, stream.TotalDuration);
        }
    }
    public void Stop(Guid id) { lock (gate) if (voices.Remove(id, out var voice)) voice.Stream?.Dispose(); }
    public void StopAll()
    {
        lock (gate)
        {
            foreach (var voice in voices.Values) voice.Stream?.Dispose();
            voices.Clear();
        }
    }
    public long TakeClippedSamples() { lock (gate) { var result = clippedSamples; clippedSamples = 0; return result; } }
    public int Read(float[] buffer, int offset, int count)
    {
        lock (gate)
        {
            // NAudio's SampleToWaveProvider passes a WaveBuffer overlay: this float[]
            // can be backed by a byte[] at runtime. Array.Clear then clears bytes,
            // not float samples, leaving old audio in the output buffer.
            // Typed element writes clear exactly the requested number of samples.
            for (var i = offset; i < offset + count; i++) buffer[i] = 0f;
            foreach (var voice in voices.Values)
            {
                if (!voice.Playing) continue;
                if (voice.Stream is not null)
                {
                    voice.Playing = voice.Stream.MixInto(buffer, offset, count, voice.Volume);
                    continue;
                }
                for (var i = 0; i < count; i++)
                {
                    if (voice.Position == voice.Samples.Length)
                    {
                        if (voice.Loop) voice.Position = 0;
                        else { voice.Playing = false; break; }
                    }
                    buffer[offset + i] += voice.Samples[voice.Position++] * voice.Volume;
                }
                if (!voice.Loop && voice.Position == voice.Samples.Length) voice.Playing = false;
            }
            for (var i = offset; i < offset + count; i++)
            {
                var sample = buffer[i] * masterVolume;
                if (sample is > 1 or < -1) clippedSamples++;
                buffer[i] = Math.Clamp(sample, -1, 1);
            }
            renderedFrames += count / WaveFormat.Channels;
            return count;
        }
    }
}






