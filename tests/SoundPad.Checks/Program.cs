using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave;
using SoundPad;
using SoundPad.Audio;
using SoundPad.Diagnostics;
using SoundPad.Models;
using SoundPad.Services;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        Console.WriteLine("PASS: " + name); checks++;
    }
    [STAThread]
    public static void Main(string[] args)
    {
        AppLog.Initialize();
        if (args.Contains("--diagnose-board"))
        {
            using var devices = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            foreach (var role in new[] { NAudio.CoreAudioApi.Role.Console, NAudio.CoreAudioApi.Role.Multimedia, NAudio.CoreAudioApi.Role.Communications })
            {
                using var device = devices.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, role);
                Console.WriteLine($"Default {role}: {device.FriendlyName}; {device.AudioClient.MixFormat}");
            }
            foreach (var endpoint in devices.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active))
            {
                using (endpoint)
                {
                    var sessions = endpoint.AudioSessionManager.Sessions;
                    for (var s = 0; s < sessions.Count; s++)
                    {
                        using var session = sessions[s];
                        try
                        {
                            using var process = System.Diagnostics.Process.GetProcessById((int)session.GetProcessID);
                            if (process.ProcessName == "SoundPad")
                                Console.WriteLine($"SoundPad session: PID {process.Id}, {endpoint.FriendlyName}, {session.State}");
                        }
                        catch (ArgumentException) { }
                    }
                }
            }
            for (var i = 0; i < WaveOut.DeviceCount; i++)
                Console.WriteLine($"WaveOut {i}: {WaveOut.GetCapabilities(i).ProductName}");
            foreach (var pad in new BoardStore().Load().Pads)
            {
                using var reader = new AudioFileReader(pad.FilePath);
                Console.WriteLine($"Source {pad.Name}: {reader.WaveFormat}; {reader.TotalTime}");
                var decoded = AudioEngine.Decode(pad.FilePath);
                var identity = new PadMixer { MasterVolume = 1 };
                identity.Start(pad.Id, decoded, false, 1);
                var position = 0;
                var chunk = new float[4098];
                double difference = 0;
                while (position < decoded.Length)
                {
                    var count = Math.Min(4096, decoded.Length - position);
                    identity.Read(chunk, 2, count);
                    for (var i = 0; i < count; i++)
                        difference = Math.Max(difference, Math.Abs(chunk[i + 2] - Math.Clamp(decoded[position + i], -1, 1)));
                    position += count;
                }
                identity.Read(chunk, 2, 4096);
                Check(difference == 0 && chunk.Skip(2).All(x => x == 0), $"No added echo, channel shift or trailing samples: {pad.Name}");
            }
            return;
        }
        var mixer = new PadMixer { MasterVolume = 1 };
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        float[] buffer = new float[8];
        mixer.Start(a, [.2f, .2f, .4f, .4f], false, 1);
        mixer.Read(buffer, 0, 8);
        Check(buffer.SequenceEqual(new float[] { .2f, .2f, .4f, .4f, 0, 0, 0, 0 }) && !mixer.IsPlaying(a), "One-shot ends and fills silence");
        mixer.Start(a, [.2f, .2f, .4f, .4f], true, 1);
        mixer.Read(buffer, 0, 8);
        Check(buffer.SequenceEqual(new float[] { .2f, .2f, .4f, .4f, .2f, .2f, .4f, .4f }), "Loop repeats within the same audio buffer");
        mixer.Stop(a); mixer.Read(buffer, 0, 8);
        Check(buffer.All(x => x == 0), "Stop silences pad");
        mixer.Start(a, [.6f, .6f], true, 1); mixer.Start(b, [.6f, .6f], true, 1);
        mixer.Read(buffer, 0, 8);
        Check(buffer.All(x => x == 1) && mixer.TakeClippedSamples() == 8, "Mixed peaks bounded and counted");
        mixer.Stop(a); mixer.Update(b, true, .5f); mixer.MasterVolume = .5f;
        mixer.Read(buffer, 0, 8);
        Check(buffer.All(x => Math.Abs(x - .15f) < .00001), "Independent stop and pad/master volume");
        mixer.Update(b, false, 1); mixer.Read(buffer, 0, 8);
        Check(!mixer.IsPlaying(b), "Loop can switch to one-shot");
        mixer.Start(a, [.2f, .2f, .4f, .4f], false, 1); mixer.MasterVolume = 1;
        mixer.Read(buffer, 0, 2); mixer.Stop(a); mixer.Start(a, [.2f, .2f, .4f, .4f], false, 1); mixer.Read(buffer, 0, 2);
        Check(buffer[0] == .2f, "Restart begins at sample zero");
        mixer.StopAll(); mixer.Read(buffer, 0, 8); Check(buffer.All(x => x == 0), "Stop all");
        // Exercise the byte-buffer adapter actually used by WASAPI/WaveOut, not only float[] calls.
        // NAudio WaveBuffer overlays byte[] as float[]; runtime Array APIs can see the byte element type.
        var byteMixer = new PadMixer { MasterVolume = 1 };
        byteMixer.Start(Guid.NewGuid(), Enumerable.Repeat(.25f, 32).ToArray(), false, 1);
        var provider = byteMixer.ToWaveProvider();
        var bytes = new byte[128];
        provider.Read(bytes, 0, bytes.Length);
        Check(Enumerable.Range(0, 32).All(i => BitConverter.ToSingle(bytes, i * 4) == .25f), "Byte adapter carries exact source samples");
        provider.Read(bytes, 0, bytes.Length);
        Check(bytes.All(x => x == 0), "Reused WASAPI byte buffer contains silence after sound ends");
        var loopId = Guid.NewGuid();
        byteMixer.Start(loopId, [.1f, -.1f, .2f, -.2f], true, 1);
        var guarded = Enumerable.Repeat((byte)0x5A, 144).ToArray();
        for (var pass = 0; pass < 5; pass++)
        {
            provider.Read(guarded, 8, 128);
            Check(Enumerable.Range(0, 32).All(i => Math.Abs(BitConverter.ToSingle(guarded, 8 + i * 4) - new float[] { .1f, -.1f, .2f, -.2f }[i % 4]) < .000001f),
                $"Reused byte buffer does not accumulate old audio, pass {pass + 1}");
        }
        Check(guarded.Take(8).Concat(guarded.Skip(136)).All(x => x == 0x5A), "Byte adapter respects nonzero offset and buffer boundaries");
        byteMixer.StopAll(); provider.Read(guarded, 8, 128);
        Check(guarded.Skip(8).Take(128).All(x => x == 0), "Stop clears every sample in reused byte buffer");
        var startMixer = new PadMixer { MasterVolume = .9f };
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        startMixer.Start(first, [.4f, .4f], true, 1, .3f);
        startMixer.Read(buffer, 0, 8);
        Check(Math.Abs(startMixer.MasterVolume - .3f) < .00001 && buffer.All(x => Math.Abs(x - .12f) < .00001), "Pad start overrides the current master level");
        startMixer.MasterVolume = .7f;
        startMixer.Start(second, [.2f, .2f], true, 1, .5f);
        startMixer.Read(buffer, 0, 8);
        Check(buffer.All(x => Math.Abs(x - .3f) < .00001), "Next pad start sets the master level for all running sounds");
        startMixer.Stop(second);
        Check(startMixer.MasterVolume == .5f, "Stopping a pad leaves the master level unchanged");
        startMixer.Start(second, [.2f, .2f], false, 1, 0);
        startMixer.Read(buffer, 0, 8);
        Check(buffer.All(x => x == 0), "Zero start volume mutes the master");
        try { startMixer.Start(Guid.NewGuid(), [], false, 1, 1); }
        catch (ArgumentException) { }
        Check(startMixer.MasterVolume == 0, "Invalid start does not change the master level");
        var selection = new[]
        {
            new Pad { IsSelected = true, StartVolume = .2, Volume = .4, IsPlaying = true },
            new Pad { IsSelected = false, StartVolume = .3, Volume = .5 },
            new Pad { IsSelected = true, StartVolume = .8, Volume = .6 }
        };
        var changes = PadSelection.ApplyStartVolume(selection, .65);
        Check(changes.Length == 2 && selection[0].StartVolume == .65 && selection[1].StartVolume == .3 && selection[2].StartVolume == .65,
            "Bulk transfer affects only selected pads");
        Check(selection[0].IsPlaying && selection[0].Volume == .4 && selection[1].Volume == .5 && selection[2].Volume == .6,
            "Bulk transfer preserves playback state and relative pad levels");
        foreach (var pad in selection) pad.IsSelected = false;
        Check(PadSelection.ApplyStartVolume(selection, .1).Length == 0 && selection[0].StartVolume == .65, "Empty selection leaves all start levels untouched");
        selection[0].IsSelected = true;
        var selectionRoundTrip = System.Text.Json.JsonSerializer.Deserialize<Pad>(System.Text.Json.JsonSerializer.Serialize(selection[0]))!;
        Check(!selectionRoundTrip.IsSelected && selectionRoundTrip.StartVolume == .65, "Start level persists, temporary selection does not");
        selection[0].IsLastPlayed = true;
        var lastPlayedRoundTrip = System.Text.Json.JsonSerializer.Deserialize<Pad>(System.Text.Json.JsonSerializer.Serialize(selection[0]))!;
        Check(selection[0].Status == "Läuft" && !lastPlayedRoundTrip.IsLastPlayed, "Last-played marker is transient and does not override active playback");
        selection[0].IsPlaying = false;
        Check(selection[0].Status == "Zuletzt gespielt", "Last-played marker remains visible after playback ends");
        var timedPad = new Pad { DurationSeconds = 245 };
        Check(timedPad.TimeLabel == "Dauer: 4:05", "Idle pad displays total duration");
        timedPad.IsPlaying = true;
        timedPad.PositionSeconds = 67;
        Check(timedPad.TimeLabel == "1:07 / 4:05", "Playing pad displays current position and total duration");
        timedPad.DurationSeconds = 3723;
        timedPad.PositionSeconds = 3661;
        Check(timedPad.TimeLabel == "1:01:01 / 1:02:03", "Time display supports files longer than one hour");
        timedPad.IsPlaying = false;
        timedPad.ResetPosition();
        Check(timedPad.TimeLabel == "Dauer: 1:02:03" && timedPad.PositionSeconds == 0, "Stopping restores duration-only display");
        var directory = Path.Combine(Path.GetTempPath(), "SoundPad-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new BoardStore(directory);
        var data = new BoardData { MasterVolume = .45, StopOtherPadsOnStart = true, UseListView = true, Pads = [new Pad { Name = "Regen", FilePath = "rain.wav", Loop = true, Volume = .3, StartVolume = .6, Color = "#2DD4BF" }] };
        store.Save(data); var restored = store.Load();
        Check(restored.Pads.Count == 1 && restored.Pads[0].Loop && restored.Pads[0].Volume == .3 && restored.Pads[0].StartVolume == .6 && restored.MasterVolume == .45 && restored.StopOtherPadsOnStart && restored.UseListView, "Settings round trip");
        var setPath = Path.Combine(directory, "abend.soundpadset");
        var setStore = BoardStore.FromFile(setPath);
        setStore.Save(data);
        Check(setStore.FilePath == Path.GetFullPath(setPath) && setStore.Load().Pads.Single().Name == "Regen", "Named set file round trip");
        var emptySetStore = BoardStore.FromFile(Path.Combine(directory, "leer.soundpadset"));
        emptySetStore.Save(new BoardData());
        Check(emptySetStore.Load().Pads.Count == 0 && emptySetStore.Load().MasterVolume == .8, "New set starts empty with default master volume");
        var activeSetStore = new ActiveSetStore(directory);
        activeSetStore.Save(setPath);
        Check(activeSetStore.Load() == Path.GetFullPath(setPath), "Last opened set path persists");
        File.WriteAllText(activeSetStore.FilePath, "{invalid");
        try { activeSetStore.Load(); throw new Exception("Corrupt active-set marker accepted"); }
        catch (System.Text.Json.JsonException) { Check(true, "Corrupt active-set marker is rejected"); }
        try { BoardStore.FromFile(" "); throw new Exception("Empty set path accepted"); }
        catch (ArgumentException) { Check(true, "Empty set path is rejected"); }
        var legacy = System.Text.Json.JsonSerializer.Deserialize<Pad>("{\"Name\":\"Alt\",\"Volume\":0.4}");
        Check(legacy?.StartVolume == .8 && legacy.Volume == .4, "Existing pads retain their relative volume and default to 80 percent start level");
        data.Pads[0].Name = "Wald"; store.Save(data);
        Check(File.Exists(store.FilePath + ".bak") && store.Load().Pads[0].Name == "Wald", "Atomic replacement preserves backup");
        File.WriteAllText(store.FilePath, "{invalid");
        try { store.Load(); throw new Exception("Corrupt board accepted"); }
        catch (System.Text.Json.JsonException) { Check(File.ReadAllText(store.FilePath) == "{invalid", "Corrupt board remains untouched"); }
        var wav = Path.Combine(directory, "mono-22050.wav");
        using (var writer = new WaveFileWriter(wav, new WaveFormat(22050, 16, 1)))
            for (var i = 0; i < 2205; i++) writer.WriteSample((float)Math.Sin(i * .1) * .1f);
        var samples = AudioEngine.Decode(wav);
        Check(samples.Length > 9000 && samples.Length < 10000 && samples.Length % 2 == 0, "WAV converted to 48kHz stereo");
        Check(Enumerable.Range(0, samples.Length / 2).All(i => samples[i * 2] == samples[i * 2 + 1]), "Mono duplicated to both channels");
        try { AudioEngine.Decode(Path.Combine(directory, "missing.wav")); throw new Exception("Missing file accepted"); }
        catch (FileNotFoundException) { Check(true, "Missing audio produces actionable failure"); }
        using (var streamed = BufferedSound.OpenAsync(wav).GetAwaiter().GetResult())
        {
            var exactMixer = new PadMixer { MasterVolume = 1 };
            var id = Guid.NewGuid();
            exactMixer.Start(id, streamed, false, 1);
            var streamedBytes = new byte[(samples.Length + 2) * 4];
            exactMixer.ToWaveProvider().Read(streamedBytes, 0, streamedBytes.Length);
            Check(Enumerable.Range(0, samples.Length).All(i => BitConverter.ToSingle(streamedBytes, i * 4) == samples[i]),
                "Streamed samples match offline decoding through the real byte adapter");
            Check(streamed.TotalDuration.TotalMilliseconds >= 99 && streamed.TotalDuration.TotalMilliseconds <= 101,
                "Streaming exposes the total duration");
            Check(streamed.Position == streamed.TotalDuration, "One-shot position reaches its total duration");            Check(!exactMixer.IsPlaying(id), "Streamed one-shot ends exactly at its boundary");
            exactMixer.Stop(id);
            Check(streamed.Completion.Wait(5000), "Stopping releases the streaming decoder");
        }
        using (var looping = BufferedSound.OpenAsync(wav).GetAwaiter().GetResult())
        {
            var loopMixer = new PadMixer { MasterVolume = 1 };
            var id = Guid.NewGuid();
            loopMixer.Start(id, looping, true, 1);
            var loopBuffer = new float[2048];
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (looping.ConsumedSamples < samples.Length * 3L && deadline.Elapsed.TotalSeconds < 5)
            {
                loopMixer.Read(loopBuffer, 0, loopBuffer.Length);
                System.Threading.Thread.Sleep(1);
            }
            Check(looping.ConsumedSamples >= samples.Length * 3L && loopMixer.IsPlaying(id), "Streaming repeats across several loop boundaries");
            Check(looping.Position >= TimeSpan.Zero && looping.Position < looping.TotalDuration,
                "Loop position wraps within the current pass");
            loopMixer.Update(id, false, 1);
            while (loopMixer.IsPlaying(id) && deadline.Elapsed.TotalSeconds < 5)
            {
                loopMixer.Read(loopBuffer, 0, loopBuffer.Length);
                System.Threading.Thread.Sleep(1);
            }
            Check(!loopMixer.IsPlaying(id), "Disabling streaming loop ends at the next boundary");
            loopMixer.StopAll();
            Check(looping.Completion.Wait(5000), "Stop all releases streaming resources");
        }
        var longWav = Path.Combine(directory, "six-minutes.wav");
        using (var writer = new WaveFileWriter(longWav, new WaveFormat(8000, 16, 1)))
        {
            var secondOfAudio = new byte[16000];
            for (var secondIndex = 0; secondIndex < 360; secondIndex++) writer.Write(secondOfAudio, 0, secondOfAudio.Length);
        }
        using (var longSound = BufferedSound.OpenAsync(longWav).GetAwaiter().GetResult())
        {
            Check(BufferedSound.MaximumBufferedBytes < 1024 * 1024, "Six-minute file opens with less than 1 MiB of sample buffering");
            var longMixer = new PadMixer { MasterVolume = 1 };
            var id = Guid.NewGuid();
            longMixer.Start(id, longSound, false, 1);
            var longBuffer = new float[8192];
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (longMixer.IsPlaying(id) && deadline.Elapsed.TotalSeconds < 30)
            {
                var before = longSound.ConsumedSamples;
                longMixer.Read(longBuffer, 0, longBuffer.Length);
                if (longSound.ConsumedSamples == before) System.Threading.Thread.Sleep(1);
            }
            Check(!longMixer.IsPlaying(id) && longSound.ConsumedSamples == 360L * 48000 * 2,
                "Entire six-minute file streams beyond the former limit");
            longMixer.StopAll();
            Check(longSound.Completion.Wait(5000), "Long-file decoder closes after playback");
        }
        if (args.Contains("--device"))
        {
            using var engine = new AudioEngine();
            engine.EnsureOutput();
            var deviceStream = engine.PrepareAsync(longWav).GetAwaiter().GetResult();
            engine.Mixer.Start(Guid.NewGuid(), deviceStream, false, 1);
            System.Threading.Thread.Sleep(200);
            Check(!engine.TryTakeError(out _) && engine.Mixer.RenderedFrames > 0 && deviceStream.ConsumedSamples > 0, "WASAPI output consumes silent audio without errors");
            var frames = engine.Mixer.RenderedFrames;
            engine.ResetOutput();
            engine.EnsureOutput();
            deviceStream = engine.PrepareAsync(longWav).GetAwaiter().GetResult();
            engine.Mixer.Start(Guid.NewGuid(), deviceStream, false, 1);
            System.Threading.Thread.Sleep(200);
            Check(!engine.TryTakeError(out _) && engine.Mixer.RenderedFrames > frames, "WASAPI output can be closed and reopened");
        }
        if (args.Contains("--render"))
        {
            var app = new App();
            var window = new MainWindow();
            window.Pads.Clear();
            string[] names = ["Regen", "Donner", "Wald", "Applaus", "Türklingel", "Intro"];
            foreach (var name in names) window.Pads.Add(new Pad { Name = name, FilePath = wav, Loop = name == "Regen", IsPlaying = name == "Regen", PositionSeconds = name == "Regen" ? 67 : 0, DurationSeconds = 245, IsSelected = name is "Regen" or "Wald" });
            // Render actual WPF controls without opening a window or touching the saved board.
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(1152, 700)); content.Arrange(new Rect(0, 0, 1152, 700)); content.UpdateLayout(); content.Measure(new Size(1152, 700)); content.Arrange(new Rect(0, 0, 1152, 700)); content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1152, 700, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            var output = Path.GetFullPath("artifacts/soundpad-ui.png");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var stream = File.Create(output); png.Save(stream);
            Console.WriteLine("Rendered: " + output);
        }
        Console.WriteLine($"{checks} checks passed. Test data: {directory}");
    }
}
















