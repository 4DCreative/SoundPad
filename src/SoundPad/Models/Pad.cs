using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using SoundPad.Services;

namespace SoundPad.Models;

public sealed class Pad : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FilePath { get; set; } = "";
    private string name = "Sound", color = "#38BDF8";
    private bool loop, playing, loading, selected, lastPlayed;
    private double volume = .8, startVolume = .8, durationSeconds, positionSeconds;
    private string? error;
    public string Name { get => name; set => Set(ref name, value); }
    public string Color { get => color; set => Set(ref color, value); }
    public bool Loop { get => loop; set { Set(ref loop, value); Changed(nameof(ModeLabel)); } }
    public double Volume { get => volume; set => Set(ref volume, Math.Clamp(value, 0, 1)); }
    public double StartVolume { get => startVolume; set => Set(ref startVolume, Math.Clamp(value, 0, 1)); }
    public double DurationSeconds { get => durationSeconds; set { Set(ref durationSeconds, Math.Max(0, value)); Changed(nameof(TimeLabel)); } }
    [JsonIgnore] public double PositionSeconds { get => positionSeconds; set { Set(ref positionSeconds, Math.Max(0, value)); Changed(nameof(TimeLabel)); } }
    [JsonIgnore] public bool IsSelected { get => selected; set => Set(ref selected, value); }
    [JsonIgnore] public bool IsLastPlayed { get => lastPlayed; set { Set(ref lastPlayed, value); Refresh(); } }
    [JsonIgnore] public bool IsPlaying { get => playing; set { Set(ref playing, value); Refresh(); } }
    [JsonIgnore] public bool IsLoading { get => loading; set { Set(ref loading, value); Refresh(); } }
    [JsonIgnore] public string? Error { get => error; set { Set(ref error, value); Refresh(); } }
    [JsonIgnore] public int Generation { get; set; }
    [JsonIgnore] public string Status => IsLoading ? Localization.Current["Loading"] : Error is not null ? Localization.Current["CheckFile"] : IsPlaying ? Localization.Current["Playing"] : IsLastPlayed ? Localization.Current["LastPlayed"] : Localization.Current["Ready"];
    [JsonIgnore] public string Symbol => IsLoading ? "…" : IsPlaying ? "■" : Error is not null ? "!" : "▶";
    [JsonIgnore] public string ModeLabel => Loop ? Localization.Current["Loop"] : Localization.Current["Once"];
    [JsonIgnore] public string TimeLabel => IsPlaying
        ? $"{FormatTime(PositionSeconds)} / {FormatTime(DurationSeconds)}"
        : DurationSeconds > 0 ? Localization.Current.Text("Duration", FormatTime(DurationSeconds)) : Localization.Current["UnknownDuration"];
    public event PropertyChangedEventHandler? PropertyChanged;
    public void ResetPosition() => PositionSeconds = 0;
    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
    private void Refresh() { Changed(nameof(Status)); Changed(nameof(Symbol)); Changed(nameof(TimeLabel)); }
    private void Changed(string name) => PropertyChanged?.Invoke(this, new(name));
    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; Changed(name);
    }
}





