using System.Windows;
using SoundPad.Models;
namespace SoundPad;
public partial class PadSettingsWindow : Window
{
    public bool RemoveRequested { get; private set; }
    public string PadName => NameInput.Text.Trim();
    public string PadColor => (string?)ColorInput.SelectedValue ?? "#38BDF8";
    public bool Loop => LoopInput.IsChecked == true;
    public double Volume => VolumeInput.Value;
    public double StartVolume => StartVolumeInput.Value;
    public PadSettingsWindow(Pad pad)
    {
        InitializeComponent();
        NameInput.Text = pad.Name; ColorInput.SelectedValue = pad.Color;
        LoopInput.IsChecked = pad.Loop; VolumeInput.Value = pad.Volume; StartVolumeInput.Value = pad.StartVolume;
        FileLabel.Text = pad.FilePath;
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PadName)) { MessageBox.Show(this, "Bitte einen Namen eingeben."); return; }
        DialogResult = true;
    }
    private void Remove(object sender, RoutedEventArgs e) { RemoveRequested = true; DialogResult = true; }
}

