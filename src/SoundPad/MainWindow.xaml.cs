using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SoundPad.Audio;
using SoundPad.Diagnostics;
using SoundPad.Models;
using SoundPad.Services;
using Loc = SoundPad.Services.Localization;

namespace SoundPad;
public partial class MainWindow : Window
{
    public ObservableCollection<Pad> Pads { get; } = [];
    public ObservableCollection<Pad> VisiblePads { get; } = [];
    private readonly AudioEngine audio = new();
    private BoardStore store = new();
    private readonly ActiveSetStore activeSetStore = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool ready, closed, savingBlocked, fullscreen, hasActiveSet, stopOtherPadsOnStart, useListView;
    private WindowState previousState;
    private int tick, currentPage, pageSize = 12;
    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        SetLanguage(Loc.Current.Language);
        try
        {
            var initialStore = ResolveInitialStore();
            if (initialStore is not null) { store = initialStore; hasActiveSet = true; }
            var data = hasActiveSet ? store.Load() : new BoardData();
            ApplyBoard(data);
            AppLog.Write("Information", "Board.Loaded", new { path = hasActiveSet ? store.FilePath : null, count = Pads.Count });
        }
        catch (Exception error)
        {
            savingBlocked = true;
            AppLog.Write("Error", "Board.LoadFailed", exception: error);
            Loaded += (_, _) => MessageBox.Show(this, "Die gespeicherte Belegung konnte nicht geladen werden. Sie bleibt unverändert. In dieser Sitzung wird nicht gespeichert.\n\n" + store.FilePath + "\n" + error.Message, "Belegung prüfen");
        }
        audio.Mixer.MasterVolume = (float)MasterSlider.Value;
        ready = true;
        UpdateSetStatus();
        Pads.CollectionChanged += (_, _) => { RefreshPage(); RefreshStatus(); };
        RefreshPage();
        timer.Tick += PollAudio;
        timer.Start();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveBoard(); };
        ContentRendered += async (_, _) =>
        {
            ((App)Application.Current).WindowReady();
            RefreshStatus();
            RefreshPage();
            await LoadMissingDurationsAsync();
        };
        PreviewKeyDown += HandlePreviewKeyDown;
    }
    private void SetGermanLanguage(object sender, RoutedEventArgs e) => SetLanguage("de");
    private void SetEnglishLanguage(object sender, RoutedEventArgs e) => SetLanguage("en");
    private void SetLanguage(string language)
    {
        Loc.Current.Language = language;
        GermanLanguageMenuItem.IsChecked = language == "de";
        EnglishLanguageMenuItem.IsChecked = language == "en";
        UpdatePlaybackModeButton(); UpdateViewMode(); UpdateSetStatus(); RefreshStatus(); RefreshPage();
    }
    private void ShowHelp(object sender, RoutedEventArgs e) => MessageBox.Show(this, Loc.Current["HelpText"], Loc.Current["Help"], MessageBoxButton.OK, MessageBoxImage.Information);
    private async void AddSounds(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Sounddateien (*.wav;*.mp3)|*.wav;*.mp3", Multiselect = true, Title = "Sounds hinzufügen" };
        if (picker.ShowDialog(this) != true) return;
        AddButton.IsEnabled = false;
        var failures = new List<string>();
        try
        {
            foreach (var path in picker.FileNames)
            {
                if (closed) break;
                try
                {
                    var duration = await audio.ValidateAsync(path);
                    if (closed) break;
                    string[] colors = ["#38BDF8", "#2DD4BF", "#FB923C", "#A78BFA", "#F472B6", "#FACC15"];
                    var pad = new Pad { Name = Path.GetFileNameWithoutExtension(path), FilePath = path, DurationSeconds = duration.TotalSeconds, StartVolume = MasterSlider.Value, Color = colors[Pads.Count % colors.Length] };
                    Pads.Add(pad);
                    AppLog.Write("Information", "Pad.Added", new { pad.Id, fileName = Path.GetFileName(path) });
                }
                catch (Exception error)
                {
                    failures.Add(Path.GetFileName(path) + ": " + error.Message);
                    AppLog.Write("Error", "Pad.ImportFailed", new { fileName = Path.GetFileName(path) }, error);
                }
            }
            if (!closed)
            {
                SaveBoard(); RefreshStatus();
                if (failures.Count > 0) MessageBox.Show(this, string.Join("\n", failures), "Einige Sounds konnten nicht hinzugefügt werden");
            }
        }
        finally { if (!closed) AddButton.IsEnabled = true; }
    }
    private async void OpenSet(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Filter = "SoundPad-Sets (*.soundpadset)|*.soundpadset|JSON-Dateien (*.json)|*.json",
            Title = "SoundPad-Set öffnen"
        };
        if (picker.ShowDialog(this) != true) return;

        try
        {
            var candidate = BoardStore.FromFile(picker.FileName);
            var data = candidate.Load();
            saveTimer.Stop(); SaveBoard();
            StopAllSounds("Audio.StopAllForSetChange");
            ApplyBoard(data);
            store = candidate;
            hasActiveSet = true;
            savingBlocked = false;
            RememberActiveSet();
            UpdateSetStatus();
            RefreshStatus();
            AppLog.Write("Information", "Set.Opened", new { path = store.FilePath, count = Pads.Count });
            await LoadMissingDurationsAsync();
        }
        catch (Exception error)
        {
            AppLog.Write("Error", "Set.OpenFailed", new { path = picker.FileName }, error);
            MessageBox.Show(this, "Das Set konnte nicht geöffnet werden. Das bisherige Set bleibt aktiv.\n\n" + error.Message, "Set öffnen");
        }
    }
    private void NewSet(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog
        {
            Filter = "SoundPad-Sets (*.soundpadset)|*.soundpadset",
            DefaultExt = ".soundpadset",
            AddExtension = true,
            FileName = "Neues Set",
            Title = "Neues SoundPad-Set anlegen"
        };
        if (picker.ShowDialog(this) != true) return;

        saveTimer.Stop();
        if (!savingBlocked && !SaveBoard()) return;
        try
        {
            var candidate = BoardStore.FromFile(picker.FileName);
            var data = new BoardData();
            candidate.Save(data);
            StopAllSounds("Audio.StopAllForNewSet");
            ApplyBoard(data);
            store = candidate;
            hasActiveSet = true;
            savingBlocked = false;
            RememberActiveSet();
            UpdateSetStatus();
            RefreshStatus();
            AppLog.Write("Information", "Set.Created", new { path = store.FilePath });
        }
        catch (Exception error)
        {
            AppLog.Write("Error", "Set.CreateFailed", new { path = picker.FileName }, error);
            MessageBox.Show(this, "Das neue Set konnte nicht angelegt werden. Das bisherige Set bleibt aktiv.\n\n" + error.Message, "Neues Set");
        }
    }
    private void SaveSetAs(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog
        {
            Filter = "SoundPad-Sets (*.soundpadset)|*.soundpadset|JSON-Dateien (*.json)|*.json",
            DefaultExt = ".soundpadset",
            AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(store.FilePath),
            Title = "SoundPad-Set speichern"
        };
        if (picker.ShowDialog(this) != true) return;

        try
        {
            var candidate = BoardStore.FromFile(picker.FileName);
            candidate.Save(CreateBoardData());
            store = candidate;
            hasActiveSet = true;
            savingBlocked = false;
            RememberActiveSet();
            UpdateSetStatus();
            RefreshStatus();
            AppLog.Write("Information", "Set.SavedAs", new { path = store.FilePath, count = Pads.Count });
        }
        catch (Exception error)
        {
            AppLog.Write("Error", "Set.SaveAsFailed", new { path = picker.FileName }, error);
            MessageBox.Show(this, "Das Set konnte nicht gespeichert werden. Das bisherige Set bleibt aktiv.\n\n" + error.Message, "Set speichern");
        }
    }
    private void ApplyBoard(BoardData data)
    {
        var wasReady = ready;
        ready = false;
        Pads.Clear();
        foreach (var pad in data.Pads)
        {
            if (!File.Exists(pad.FilePath)) pad.Error = "Die Sounddatei wurde verschoben oder gelöscht.";
            Pads.Add(pad);
        }
        MasterSlider.Value = data.MasterVolume;
        stopOtherPadsOnStart = data.StopOtherPadsOnStart;
        useListView = data.UseListView;
        audio.Mixer.MasterVolume = (float)data.MasterVolume;
        UpdatePlaybackModeButton();
        UpdateViewMode();
        ready = wasReady;
    }
    private async void TogglePad(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Pad pad) return;
        if (pad.IsPlaying || pad.IsLoading) { StopPad(pad, "Pad.Stopped"); RefreshStatus(); return; }
        if (stopOtherPadsOnStart) StopOtherPads(pad);
        var generation = ++pad.Generation;
        pad.Error = null; pad.IsLoading = true;
        var watch = Stopwatch.StartNew();
        BufferedSound? stream = null;
        try
        {
            stream = await audio.PrepareAsync(pad.FilePath);
            if (closed || generation != pad.Generation || !Pads.Contains(pad)) return;
            pad.DurationSeconds = stream.TotalDuration.TotalSeconds;
            pad.ResetPosition();
            audio.EnsureOutput();
            var previousMasterVolume = MasterSlider.Value;
            audio.Mixer.Start(pad.Id, stream, pad.Loop, (float)pad.Volume, (float)pad.StartVolume);
            stream = null; // The mixer owns it after a successful start.
            MasterSlider.Value = pad.StartVolume;
            AppLog.Write("Information", "Audio.StartVolumeApplied", new { pad.Id, previousMasterVolume, masterVolume = pad.StartVolume });
            pad.IsPlaying = true;
            MarkLastPlayed(pad);
            AppLog.Write("Information", "Pad.Started", new { pad.Id, pad.Loop, pad.Volume, preparationMs = watch.Elapsed.TotalMilliseconds });
        }
        catch (Exception error)
        {
            if (closed || generation != pad.Generation) return;
            pad.Error = error.Message;
            AppLog.Write("Error", "Pad.StartFailed", new { pad.Id }, error);
            MessageBox.Show(this, error.Message, "Sound konnte nicht gestartet werden");
        }
        finally
        {
            stream?.Dispose();
            if (!closed && generation == pad.Generation) { pad.IsLoading = false; RefreshStatus(); }
        }
    }
    private void ToggleListPad(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is System.Windows.Controls.Button or System.Windows.Controls.CheckBox) return;
        }
        TogglePad(sender, e);
    }
    private void StopPad(Pad pad, string action)
    {
        pad.Generation++; pad.IsLoading = false; pad.IsPlaying = false;
        var underruns = audio.Mixer.GetUnderruns(pad.Id);
        if (underruns > 0) AppLog.Write("Warning", "Audio.StreamUnderruns", new { pad.Id, underruns });
        audio.Mixer.Stop(pad.Id);
        pad.ResetPosition();
        AppLog.Write("Information", action, new { pad.Id });
    }
    private void StopOtherPads(Pad except)
    {
        var padsToStop = Pads.Where(pad => pad != except && (pad.IsPlaying || pad.IsLoading)).ToArray();
        foreach (var pad in padsToStop) StopPad(pad, "Pad.StoppedForExclusiveStart");
        if (padsToStop.Length > 0) AppLog.Write("Information", "Audio.StopOthers", new { except.Id, count = padsToStop.Length });
    }
    private void MarkLastPlayed(Pad lastPlayed)
    {
        foreach (var pad in Pads) pad.IsLastPlayed = pad == lastPlayed;
    }
    private void HandlePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && fullscreen)
        {
            ToggleFullScreen(this, new());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase)
        {
            StopAllSounds("UI.StopAllKeyboard");
            e.Handled = true;
        }
    }
    private void StopAll(object sender, RoutedEventArgs e) => StopAllSounds("Audio.StopAll");
    private void StopAllSounds(string action)
    {
        foreach (var pad in Pads) { pad.Generation++; pad.IsLoading = false; pad.IsPlaying = false; pad.ResetPosition(); }
        audio.Mixer.StopAll();
        AppLog.Write("Information", action, new { source = action == "UI.StopAllKeyboard" ? "keyboard" : "button" });
        RefreshStatus();
    }
    private void EditPad(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Pad pad) return;
        var dialog = new PadSettingsWindow(pad) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (dialog.RemoveRequested)
        {
            StopPad(pad, "Pad.Removed"); Pads.Remove(pad);
        }
        else
        {
            pad.Name = dialog.PadName; pad.Color = dialog.PadColor; pad.Loop = dialog.Loop; pad.Volume = dialog.Volume; pad.StartVolume = dialog.StartVolume;
            audio.Mixer.Update(pad.Id, pad.Loop, (float)pad.Volume);
            AppLog.Write("Information", "Pad.SettingsChanged", new { pad.Id, pad.Loop, pad.Volume, pad.StartVolume, pad.Color });
        }
        SaveBoard(); RefreshStatus();
    }
    private void PadSelectionChanged(object sender, RoutedEventArgs e)
    {
        // The checkbox is a sibling of the playback button, so selection never starts audio.
        TransferStatus.Text = "";
        RefreshStatus();
    }
    private void SelectAllPads(object sender, RoutedEventArgs e)
    {
        var select = Pads.Any(p => !p.IsSelected);
        foreach (var pad in Pads) pad.IsSelected = select;
        TransferStatus.Text = "";
        RefreshStatus();
    }
    private void ApplyVolumeToSelection(object sender, RoutedEventArgs e)
    {
        var changes = PadSelection.ApplyStartVolume(Pads, MasterSlider.Value);
        if (changes.Length == 0) return;
        AppLog.Write("Information", "Pads.StartVolumeTransferred", new { count = changes.Length, changes });
        var saved = SaveBoard();
        TransferStatus.Text = saved ? $"{changes.Length} Pads: {MasterSlider.Value:P0}" : "Nicht gespeichert";
        RefreshStatus();
    }
    private void MasterChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready) return;
        audio.Mixer.MasterVolume = (float)e.NewValue;
        saveTimer.Stop(); saveTimer.Start();
    }
    private bool SaveBoard()
    {
        if (savingBlocked) return false;
        if (!hasActiveSet) return true;
        try
        {
            store.Save(CreateBoardData());
            AppLog.Write("Information", "Board.Saved", new { path = store.FilePath, count = Pads.Count, masterVolume = MasterSlider.Value });
            return true;
        }
        catch (Exception error)
        {
            AppLog.Write("Error", "Board.SaveFailed", exception: error);
            MessageBox.Show(this, "Die Änderungen konnten nicht gespeichert werden.\n" + error.Message, "Speicherfehler");
            return false;
        }
    }
    private BoardData CreateBoardData() => new() { Pads = Pads.ToList(), MasterVolume = MasterSlider.Value, StopOtherPadsOnStart = stopOtherPadsOnStart, UseListView = useListView };
    private void TogglePlaybackMode(object sender, RoutedEventArgs e)
    {
        stopOtherPadsOnStart = !stopOtherPadsOnStart;
        UpdatePlaybackModeButton();
        SaveBoard();
        AppLog.Write("Information", "Playback.ModeChanged", new { stopOtherPadsOnStart });
    }
    private void UpdatePlaybackModeButton()
    {
        PlaybackModeButton.Content = stopOtherPadsOnStart ? Loc.Current["Exclusive"] : Loc.Current["Additive"];
        PlaybackModeButton.ToolTip = stopOtherPadsOnStart
            ? "Ein neues Pad beendet alle anderen laufenden Pads"
            : "Ein neues Pad startet zusätzlich zu laufenden Pads";
    }
    private void ToggleViewMode(object sender, RoutedEventArgs e)
    {
        useListView = !useListView;
        UpdateViewMode();
        SaveBoard();
        AppLog.Write("Information", "UI.ViewModeChanged", new { view = useListView ? "list" : "pads" });
    }
    private void UpdateViewMode()
    {
        PadGrid.Visibility = useListView ? Visibility.Collapsed : Visibility.Visible;
        PadListScroll.Visibility = useListView ? Visibility.Visible : Visibility.Collapsed;
        ViewModeButton.Content = useListView ? Loc.Current["PadView"] : Loc.Current["ListView"];
        ViewModeButton.ToolTip = useListView ? "Zur Pad-Ansicht wechseln" : "Zur Listenansicht wechseln";
        UpdatePageSize();
        RefreshPage();
    }
    private BoardStore? ResolveInitialStore()
    {
        try
        {
            var path = activeSetStore.Load();
            if (path is null) return null;
            if (File.Exists(path)) return BoardStore.FromFile(path);
            AppLog.Write("Warning", "Set.LastOpenedMissing", new { path });
        }
        catch (Exception error)
        {
            AppLog.Write("Warning", "Set.LastOpenedReadFailed", exception: error);
        }
        return null;
    }
    private void RememberActiveSet()
    {
        try
        {
            activeSetStore.Save(store.FilePath);
            AppLog.Write("Information", "Set.LastOpenedSaved", new { path = store.FilePath });
        }
        catch (Exception error)
        {
            AppLog.Write("Error", "Set.LastOpenedSaveFailed", new { path = store.FilePath }, error);
        }
    }
    private void UpdateSetStatus()
    {
        SetStatus.Text = hasActiveSet ? Loc.Current["SetPrefix"] + Path.GetFileNameWithoutExtension(store.FilePath) : Loc.Current["NoSet"];
        SetStatus.ToolTip = hasActiveSet ? store.FilePath : "Ein neues oder gespeichertes Set auswählen";
    }
    private void PollAudio(object? sender, EventArgs e)
    {
        foreach (var pad in Pads.Where(x => x.IsPlaying))
        {
            var time = audio.Mixer.GetPlaybackTime(pad.Id);
            if (time.HasValue)
            {
                pad.DurationSeconds = time.Value.Duration.TotalSeconds;
                pad.PositionSeconds = time.Value.Position.TotalSeconds;
            }
        }
        foreach (var pad in Pads.Where(x => x.IsPlaying && !audio.Mixer.IsPlaying(x.Id)))
        {
            pad.IsPlaying = false;
            pad.ResetPosition();
            var failure = audio.Mixer.GetError(pad.Id);
            var underruns = audio.Mixer.GetUnderruns(pad.Id);
            audio.Mixer.Stop(pad.Id);
            pad.Error = failure?.Message;
            AppLog.Write(failure is null ? "Information" : "Error", failure is null ? "Pad.Completed" : "Pad.StreamFailed", new { pad.Id, underruns }, failure);
        }
        while (audio.TryTakeError(out var error))
        {
            StopAll(this, new()); audio.ResetOutput();
            AppLog.Write("Error", "Audio.OutputFailed", exception: error);
            MessageBox.Show(this, "Das Audiogerät ist nicht verfügbar. Prüfe die Windows-Audioausgabe und tippe danach erneut auf ein Pad.\n" + error?.Message, "Audioausgabe");
        }
        if (++tick % 50 == 0)
        {
            var clipping = audio.Mixer.TakeClippedSamples();
            if (clipping > 0) AppLog.Write("Warning", "Audio.Clipping", new { clippedSamples = clipping, hint = "Pad- oder Gesamtlautstärke reduzieren" });
        }
        RefreshStatus();
    }
    private async Task LoadMissingDurationsAsync()
    {
        var changed = false;
        foreach (var pad in Pads.Where(p => p.DurationSeconds <= 0 && File.Exists(p.FilePath)).ToArray())
        {
            try
            {
                pad.DurationSeconds = (await AudioEngine.GetDurationAsync(pad.FilePath)).TotalSeconds;
                changed = true;
            }
            catch (Exception error)
            {
                AppLog.Write("Warning", "Pad.DurationReadFailed", new { pad.Id }, error);
            }
        }
        if (changed && !closed) SaveBoard();
    }
    private void RefreshStatus()
    {
        var selected = Pads.Count(p => p.IsSelected);
        SelectionStatus.Text = Loc.Current.Text("Selected", selected);
        ApplyVolumeButton.IsEnabled = selected > 0 && !savingBlocked;
        SelectAllButton.IsEnabled = Pads.Count > 0;
        SelectAllButton.Content = Pads.Count > 0 && selected == Pads.Count ? Loc.Current["ClearSelection"] : Loc.Current["SelectAll"];
        EmptyState.Visibility = Pads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActiveStatus.Text = Loc.Current.Text("ActiveSounds", Pads.Count(x => x.IsPlaying), Pads.Count);
        LogStatus.Text = savingBlocked ? "Belegung nicht lesbar – Speichern ist für diese Sitzung deaktiviert."
            : AppLog.LastWriteError is not null ? "Loggingfehler: " + AppLog.LastWriteError
            : "Antippen: Start / Stopp · Leertaste: Alle stoppen · ⚙: Pad bearbeiten";
    }
    private void PadAreaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePageSize();
    }
    private void UpdatePageSize()
    {
        const double minimumPadSize = 160;
        if (PadArea.ActualWidth <= 0 || PadArea.ActualHeight <= 0) return;
        if (useListView)
        {
            pageSize = Math.Max(1, Pads.Count);
            RefreshPage();
            return;
        }
        var columns = Math.Max(1, (int)(PadArea.ActualWidth / minimumPadSize));
        var rowHeight = useListView ? 79 : minimumPadSize;
        var rows = Math.Max(1, (int)(PadArea.ActualHeight / rowHeight));
        var newPageSize = Math.Clamp(columns * rows, 1, 18);
        if (newPageSize == pageSize) return;
        pageSize = newPageSize;
        RefreshPage();
    }
    private void RefreshPage()
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(Pads.Count / (double)pageSize));
        currentPage = Math.Clamp(currentPage, 0, pageCount - 1);
        var visible = Pads.Skip(currentPage * pageSize).Take(pageSize).ToArray();
        VisiblePads.Clear();
        foreach (var pad in visible) VisiblePads.Add(pad);
        PageNavigation.Visibility = pageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        PreviousPageButton.IsEnabled = currentPage > 0;
        NextPageButton.IsEnabled = currentPage < pageCount - 1;
        PageStatus.Text = Loc.Current.Text("Page", currentPage + 1, pageCount);
    }
    private void PreviousPage(object sender, RoutedEventArgs e)
    {
        if (currentPage == 0) return;
        currentPage--;
        RefreshPage();
    }
    private void NextPage(object sender, RoutedEventArgs e)
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(Pads.Count / (double)pageSize));
        if (currentPage >= pageCount - 1) return;
        currentPage++;
        RefreshPage();
    }
    private void OpenLogs(object sender, RoutedEventArgs e)
    {
        try { AppLog.Write("Information", "UI.OpenLogDirectory"); Process.Start(new ProcessStartInfo(AppLog.DirectoryPath) { UseShellExecute = true }); }
        catch (Exception error) { AppLog.Write("Error", "UI.OpenLogDirectoryFailed", exception: error); MessageBox.Show(this, error.Message); }
    }
    private void ToggleFullScreen(object sender, RoutedEventArgs e)
    {
        if (!fullscreen) { previousState = WindowState; WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; }
        else { WindowState = WindowState.Normal; WindowStyle = WindowStyle.SingleBorderWindow; WindowState = previousState; }
        fullscreen = !fullscreen;
        AppLog.Write("Information", "UI.FullscreenChanged", new { fullscreen });
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        saveTimer.Stop(); SaveBoard();
        base.OnClosing(e);
    }
    protected override void OnClosed(EventArgs e)
    {
        closed = true; timer.Stop(); saveTimer.Stop();
        foreach (var pad in Pads) pad.Generation++;
        audio.Dispose();
        base.OnClosed(e);
    }
}










