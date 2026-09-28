using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Markup;

namespace SoundPad.Services;

public sealed class Localization : INotifyPropertyChanged
{
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundPad", "language.txt");
    public static Localization Current { get; } = new();
    private string language = LoadLanguage();

    private static readonly IReadOnlyDictionary<string, string> German = new Dictionary<string, string>
    {
        ["Tagline"] = "Deine Sounds. Ein Fingertipp.", ["AddSound"] = "+ Sound hinzufügen", ["NewSet"] = "Neues Set", ["OpenSet"] = "Set öffnen", ["SaveSetAs"] = "Set speichern unter…", ["Fullscreen"] = "Vollbild", ["Logs"] = "Logs",
        ["MasterVolume"] = "Gesamtlautstärke", ["Selected"] = "{0} ausgewählt", ["SelectAll"] = "Alle auswählen", ["ClearSelection"] = "Auswahl aufheben", ["ApplySelection"] = "Auf Auswahl\nübertragen",
        ["EmptyTitle"] = "Dein Soundboard beginnt hier", ["EmptyAdd"] = "Füge WAV- oder MP3-Dateien hinzu.", ["EmptyHint"] = "Antippen startet. Erneut antippen stoppt.",
        ["StopAll"] = "■  Alle stoppen", ["Previous"] = "‹ Zurück", ["Next"] = "Weiter ›", ["Page"] = "Seite {0} von {1}", ["ListView"] = "Listenansicht", ["PadView"] = "Pad-Ansicht",
        ["Additive"] = "Zusätzlich starten", ["Exclusive"] = "Anderes Pad stoppt", ["NoSet"] = "Kein Set geöffnet", ["SetPrefix"] = "Set: ",
        ["Loading"] = "Lädt …", ["CheckFile"] = "Datei prüfen", ["Playing"] = "Läuft", ["LastPlayed"] = "Zuletzt gespielt", ["Ready"] = "Bereit", ["Loop"] = "↻  Schleife", ["Once"] = "Einmal", ["Duration"] = "Dauer: {0}", ["UnknownDuration"] = "Dauer: --:--",
        ["EditPad"] = "Pad bearbeiten", ["Name"] = "Name", ["Color"] = "Farbe", ["Blue"] = "Blau", ["Turquoise"] = "Türkis", ["Orange"] = "Orange", ["Violet"] = "Violett", ["Pink"] = "Rosa", ["Yellow"] = "Gelb", ["RepeatLoop"] = "↻  Sound in Schleife wiederholen", ["StartVolume"] = "Startlautstärke (Gesamtlautstärke)", ["PadVolume"] = "Pad-Lautstärke (relative Lautstärke)", ["Save"] = "Speichern", ["Cancel"] = "Abbrechen", ["RemovePad"] = "Pad entfernen",
        ["Language"] = "Sprache", ["Help"] = "Hilfe", ["HelpText"] = "SoundPad verwenden\n\n• Antippen eines Pads startet oder stoppt den Sound.\n• Mit der Leertaste werden alle Sounds gestoppt.\n• Das Zahnrad öffnet die Einstellungen des jeweiligen Pads.\n• In der Listenansicht startet oder stoppt ein Tipp auf die ganze Zeile den Sound.\n• Mit ‚Anderes Pad stoppt‘ wird beim Start eines Pads jeder andere laufende Sound beendet.", ["ActiveSounds"] = "{0} Sounds aktiv · {1} Pads", ["PadSelection"] = "Pad auswählen", ["Edit"] = "Pad bearbeiten"
    };
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["Tagline"] = "Your sounds. One tap.", ["AddSound"] = "+ Add sound", ["NewSet"] = "New set", ["OpenSet"] = "Open set", ["SaveSetAs"] = "Save set as…", ["Fullscreen"] = "Fullscreen", ["Logs"] = "Logs",
        ["MasterVolume"] = "Master volume", ["Selected"] = "{0} selected", ["SelectAll"] = "Select all", ["ClearSelection"] = "Clear selection", ["ApplySelection"] = "Apply to\nselection",
        ["EmptyTitle"] = "Your soundboard starts here", ["EmptyAdd"] = "Add WAV or MP3 files.", ["EmptyHint"] = "Tap to start. Tap again to stop.",
        ["StopAll"] = "■  Stop all", ["Previous"] = "‹ Previous", ["Next"] = "Next ›", ["Page"] = "Page {0} of {1}", ["ListView"] = "List view", ["PadView"] = "Pad view",
        ["Additive"] = "Start alongside", ["Exclusive"] = "New pad stops others", ["NoSet"] = "No set open", ["SetPrefix"] = "Set: ",
        ["Loading"] = "Loading …", ["CheckFile"] = "Check file", ["Playing"] = "Playing", ["LastPlayed"] = "Last played", ["Ready"] = "Ready", ["Loop"] = "↻  Loop", ["Once"] = "Once", ["Duration"] = "Duration: {0}", ["UnknownDuration"] = "Duration: --:--",
        ["EditPad"] = "Edit pad", ["Name"] = "Name", ["Color"] = "Color", ["Blue"] = "Blue", ["Turquoise"] = "Turquoise", ["Orange"] = "Orange", ["Violet"] = "Violet", ["Pink"] = "Pink", ["Yellow"] = "Yellow", ["RepeatLoop"] = "↻  Repeat sound in a loop", ["StartVolume"] = "Start volume (master volume)", ["PadVolume"] = "Pad volume (relative volume)", ["Save"] = "Save", ["Cancel"] = "Cancel", ["RemovePad"] = "Remove pad",
        ["Language"] = "Language", ["Help"] = "Help", ["HelpText"] = "Using SoundPad\n\n• Tap a pad to start or stop its sound.\n• Press Space to stop all sounds.\n• The gear icon opens that pad's settings.\n• In list view, tap anywhere on a row to start or stop its sound.\n• With ‘New pad stops others’, starting a pad stops every other sound.", ["ActiveSounds"] = "{0} sounds active · {1} pads", ["PadSelection"] = "Select pad", ["Edit"] = "Edit pad"
    };

    public string Language { get => language; set { if (value is not "de" and not "en" || language == value) return; language = value; Persist(); Changed("Item[]"); Changed(nameof(Language)); } }
    public string this[string key] => (Language == "en" ? English : German).TryGetValue(key, out var value) ? value : key;
    public string Text(string key, params object[] values) => string.Format(this[key], values);
    public event PropertyChangedEventHandler? PropertyChanged;
    private static string LoadLanguage() => File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Trim() == "en" ? "en" : "de";
    private void Persist() { try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, language); } catch { } }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding($"[{Key}]") { Source = Localization.Current }.ProvideValue(serviceProvider);
}



