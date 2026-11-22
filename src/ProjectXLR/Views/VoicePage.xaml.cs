using System.Windows;
using System.Windows.Controls;
using ProjectXLR.ViewModels;

namespace ProjectXLR.Views;

public partial class VoicePage : UserControl
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public VoicePage() => InitializeComponent();

    private static string? TagOf(object sender) => (sender as FrameworkElement)?.Tag?.ToString();

    private void MicGainStep_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TagOf(sender), out int delta)) Vm.AdjustMicGain(delta);
    }

    private void SidetoneStep_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TagOf(sender), out int delta)) Vm.AdjustSidetoneVolume(delta);
    }

    private void GatePreset_Click(object sender, RoutedEventArgs e)
    {
        if (TagOf(sender) is { } mode) Vm.SetGatePreset(mode);
    }

    private void EqPreset_Click(object sender, RoutedEventArgs e)
    {
        if (TagOf(sender) is { } mode) Vm.SetEqPreset(mode);
    }

    private void CompressorPreset_Click(object sender, RoutedEventArgs e)
    {
        if (TagOf(sender) is { } mode) Vm.SetCompressorPreset(mode);
    }

    private void DeEsserPreset_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TagOf(sender), out int pct)) Vm.SetDeEsserPreset(pct);
    }

    private void BleepPreset_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TagOf(sender), out int db)) Vm.SetBleepPreset(db);
    }

    private void MicPresetBroadcast_Click(object sender, RoutedEventArgs e) => Vm.ApplyMicFilterPreset("BroadcastClean");

    private void MicPresetNoisyRoom_Click(object sender, RoutedEventArgs e) => Vm.ApplyMicFilterPreset("NoisyRoom");

    private void MicPresetFlat_Click(object sender, RoutedEventArgs e) => Vm.ApplyMicFilterPreset("FlatBypass");

    private void AutoHarmonizeSuppression_Click(object sender, RoutedEventArgs e) => Vm.AutoHarmonizeSuppression();

    private async void InstallOrRepairDeepFilterNet_Click(object sender, RoutedEventArgs e) => await Vm.InstallOrRepairDeepFilterNetAsync();

    private void CheckMyVoice_Click(object sender, RoutedEventArgs e) => Vm.OpenVoiceCheck();

    private void ToggleMicCheck_Click(object sender, RoutedEventArgs e) => Vm.ToggleMicCheck();
}
