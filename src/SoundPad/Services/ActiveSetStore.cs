using System.IO;
using System.Text.Json;

namespace SoundPad.Services;

public sealed class ActiveSetStore
{
    public string FilePath { get; }

    public ActiveSetStore(string? directory = null)
    {
        FilePath = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundPad"), "active-set.json");
    }

    public string? Load()
    {
        if (!File.Exists(FilePath)) return null;
        var data = JsonSerializer.Deserialize<ActiveSetData>(File.ReadAllText(FilePath));
        if (data is null || string.IsNullOrWhiteSpace(data.Path)) throw new InvalidDataException("Das zuletzt verwendete Set ist nicht angegeben.");
        return Path.GetFullPath(data.Path);
    }

    public void Save(string setPath)
    {
        if (string.IsNullOrWhiteSpace(setPath)) throw new ArgumentException("Ein Set-Dateiname fehlt.", nameof(setPath));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new ActiveSetData { Path = Path.GetFullPath(setPath) }));
        File.Move(temporary, FilePath, true);
    }

    private sealed class ActiveSetData
    {
        public string? Path { get; set; }
    }
}
