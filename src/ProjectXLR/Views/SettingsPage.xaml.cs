using System.Windows;
using System.Windows.Controls;
using ProjectXLR.ViewModels;

namespace ProjectXLR.Views;

public partial class SettingsPage : UserControl
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public SettingsPage() => InitializeComponent();

    private void OpenQuickStart_Click(object sender, RoutedEventArgs e) => Vm.OpenQuickStartWizard();

    private void SelectProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string name) Vm.SelectedProfileName = name;
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e) => Vm.SaveCurrentProfile();

    private void DuplicateProfile_Click(object sender, RoutedEventArgs e) => Vm.CreateOrDuplicateProfile();

    private void DeleteProfile_Click(object sender, RoutedEventArgs e) => Vm.DeleteCurrentProfile();

    private void ImportOfficialProfiles_Click(object sender, RoutedEventArgs e) => Vm.ImportOfficialProfiles();

    private void RouteWindowsAudioToGoXlr_Click(object sender, RoutedEventArgs e) => Vm.SetGoXlrAsDefaultWindowsAudio();

    private async void InstallOrRepairDeepFilterNet_Click(object sender, RoutedEventArgs e) => await Vm.InstallOrRepairDeepFilterNetAsync();

    private void RefreshDeepFilterNetStatus_Click(object sender, RoutedEventArgs e) => Vm.RefreshDeepFilterNetStatus();

    private void OpenDeepFilterNetConfigurator_Click(object sender, RoutedEventArgs e) => Vm.OpenDeepFilterNetConfigurator();

    private void LaunchAltDenoiser_Click(object sender, RoutedEventArgs e) => Vm.LaunchAltDenoiserStandalone();

    private void KillLegacyGoXlr_Click(object sender, RoutedEventArgs e) => Vm.KillLegacyGoXlrProcess();

    private void OpenWindowsApps_Click(object sender, RoutedEventArgs e) => Vm.OpenWindowsAppsFeatures();

    private void OpenWindowsSoundControlPanel_Click(object sender, RoutedEventArgs e) => Vm.OpenWindowsSoundControlPanel();

    private void ExitApp_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.FullExit();
}
