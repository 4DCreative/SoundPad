using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using SoundPad.Models;

namespace SoundPad.Services;

public sealed class BoardData
{
    public int Version { get; set; } = 1;
    public double MasterVolume { get; set; } = .8;
    public bool StopOtherPadsOnStart { get; set; }
    public bool UseListView { get; set; }
    public List<Pad> Pads { get; set; } = [];
}

public sealed class BoardStore
{
    public string FilePath { get; }

    public BoardStore(string? directory = null)
    {
        FilePath = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundPad"), "board.json");
    }

    private BoardStore(string filePath, bool isFilePath)
    {
        FilePath = Path.GetFullPath(filePath);
    }

    public static BoardStore FromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Ein Set-Dateiname fehlt.", nameof(filePath));
        return new BoardStore(filePath, true);
    }
    public BoardData Load()
    {
        if (!File.Exists(FilePath)) return new();
        var data = JsonSerializer.Deserialize<BoardData>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("Die Belegung ist leer.");
        if (data.Version != 1 || data.Pads is null || !double.IsFinite(data.MasterVolume)) throw new InvalidDataException("Unbekanntes oder ungültiges Belegungsformat.");
        var ids = new HashSet<Guid>();
        foreach (var pad in data.Pads)
        {
            if (pad is null || pad.Id == Guid.Empty || !ids.Add(pad.Id) || string.IsNullOrWhiteSpace(pad.Name) || string.IsNullOrWhiteSpace(pad.FilePath)
                || pad.Color is null || !Regex.IsMatch(pad.Color, "^#[0-9a-fA-F]{6}$") || !double.IsFinite(pad.Volume) || !double.IsFinite(pad.StartVolume)
                || !double.IsFinite(pad.DurationSeconds) || pad.DurationSeconds < 0)
                throw new InvalidDataException("Die Belegung enthält ungültige Pad-Daten.");
        }
        data.MasterVolume = Math.Clamp(data.MasterVolume, 0, 1);
        return data;
    }
    public void Save(BoardData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
        else File.Move(temporary, FilePath);
    }
}


