using SoundPad.Models;
namespace SoundPad.Services;

public static class PadSelection
{
    public sealed record VolumeChange(Guid PadId, double PreviousStartVolume, double StartVolume);
    public static VolumeChange[] ApplyStartVolume(IEnumerable<Pad> pads, double volume)
    {
        if (!double.IsFinite(volume) || volume < 0 || volume > 1)
            throw new ArgumentOutOfRangeException(nameof(volume));
        var selected = pads.Where(p => p.IsSelected).ToArray();
        var changes = selected.Select(p => new VolumeChange(p.Id, p.StartVolume, volume)).ToArray();
        foreach (var pad in selected) pad.StartVolume = volume;
        return changes;
    }
}
