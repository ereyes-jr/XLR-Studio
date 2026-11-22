using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ProjectXLR.Hardware;
using ProjectXLR.Models;
using ProjectXLR.ViewModels;

namespace ProjectXLR;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _isExplicitExit = false;
    private System.Threading.EventWaitHandle? _bringToFrontEvent;
    private System.Threading.RegisteredWaitHandle? _registeredWaitHandle;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        SourceInitialized += (_, _) => UseDarkTitleBar();
        Activated += (_, _) => { if (_vm.IsSettingsTab) _vm.RefreshLegacyGoXlrStatus(); };

        if (!MainViewModel.PreviewMode)
        {
            InitializeTrayIcon();
            InitializeSingleInstanceEvent();
        }

        if (System.Windows.Application.Current != null)
        {
            System.Windows.Application.Current.SessionEnding += (s, ev) =>
            {
                _isExplicitExit = true;
            };
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.IsCompactWindowMode = e.NewSize.Width < 960;
        }
    }

    private void Reconnect_Click(object sender, RoutedEventArgs e) =>
        _ = _vm.ReconnectDeviceAsync();

    private void MicGainPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && int.TryParse(elem.Tag?.ToString(), out int gain))
        {
            _vm.SetMicGainPreset(gain);
        }
    }

    private void CloseQuickStart_Click(object sender, RoutedEventArgs e) =>
        _vm.CloseQuickStartWizard();

    private void QuickStartNext_Click(object sender, RoutedEventArgs e) =>
        _vm.QuickStartNext();

    private void QuickStartPrev_Click(object sender, RoutedEventArgs e) =>
        _vm.QuickStartPrev();

    private void CoachFix_Click(object sender, RoutedEventArgs e) => _vm.ApplyCoachFix();

    private void CoachDismiss_Click(object sender, RoutedEventArgs e) => _vm.DismissCoachHint();

    private void VoiceCheckStart_Click(object sender, RoutedEventArgs e) => _vm.StartVoiceCheck();

    private void VoiceCheckClose_Click(object sender, RoutedEventArgs e) => _vm.CloseVoiceCheck();

    private void VoiceCheckUndo_Click(object sender, RoutedEventArgs e) => _vm.UndoVoiceCheck();

    private void VoiceCheckListen_Click(object sender, RoutedEventArgs e) => _vm.ToggleMicCheck();

    private void VoiceCheckToneNatural_Click(object sender, RoutedEventArgs e) => _vm.SetToneNaturalFromVoiceCheck();

    private void VoiceCheckAiGentle_Click(object sender, RoutedEventArgs e) => _vm.SetAiStrengthFromVoiceCheck("Gentle");

    private void VoiceCheckAiStrongest_Click(object sender, RoutedEventArgs e) => _vm.SetAiStrengthFromVoiceCheck("Aggressive");

    private void UseDarkTitleBar()
    {
        try
        {
            int enabled = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int));
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void InitializeTrayIcon()
    {
        try
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();

            string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                try { _trayIcon.Icon = new System.Drawing.Icon(iconPath); } catch { }
            }

            if (_trayIcon.Icon == null)
            {
                try
                {
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                    {
                        _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    }
                }
                catch { }
            }

            if (_trayIcon.Icon == null)
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            _trayIcon.Text = "XLR Studio - Active";
            _trayIcon.Visible = true;

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();

            var openItem = new System.Windows.Forms.ToolStripMenuItem("Open XLR Studio");
            openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
            openItem.Click += (s, e) => RestoreFromTray();
            contextMenu.Items.Add(openItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var sidetoneItem = new System.Windows.Forms.ToolStripMenuItem("Toggle Sidetone (Hear Myself)");
            sidetoneItem.Checked = _vm.SidetoneEnabled;
            sidetoneItem.Click += (s, e) =>
            {
                _vm.SidetoneEnabled = !_vm.SidetoneEnabled;
                sidetoneItem.Checked = _vm.SidetoneEnabled;
            };
            contextMenu.Items.Add(sidetoneItem);

            var soundSettingsItem = new System.Windows.Forms.ToolStripMenuItem("Windows Sound Settings");
            soundSettingsItem.Click += (s, e) => WindowsAudioEndpointManager.OpenWindowsSoundControlPanel();
            contextMenu.Items.Add(soundSettingsItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit XLR Studio");
            exitItem.Click += (s, e) => FullExit();
            contextMenu.Items.Add(exitItem);

            contextMenu.Opening += (s, e) =>
            {
                sidetoneItem.Checked = _vm.SidetoneEnabled;
            };

            _trayIcon.ContextMenuStrip = contextMenu;

            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    RestoreFromTray();
                }
            };
        }
        catch (Exception ex)
        {
            ProfileManager.LogError("MainWindow.InitializeTrayIcon", ex);
        }
    }

    private void InitializeSingleInstanceEvent()
    {
        try
        {
            _bringToFrontEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "ProjectXLR_BringToFront_Event");
            _registeredWaitHandle = System.Threading.ThreadPool.RegisterWaitForSingleObject(
                _bringToFrontEvent,
                (state, timedOut) =>
                {
                    if (!timedOut)
                    {
                        Dispatcher.InvokeAsync(RestoreFromTray);
                    }
                },
                null,
                -1,
                false);
        }
        catch (Exception ex)
        {
            ProfileManager.LogError("MainWindow.InitializeSingleInstanceEvent", ex);
        }
    }

    public void RestoreFromTray()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(RestoreFromTray);
            return;
        }

        try
        {
            if (!IsVisible)
            {
                Show();
            }
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }
        catch { }
    }

    public void FullExit()
    {
        _isExplicitExit = true;
        try
        {
            _registeredWaitHandle?.Unregister(null);
            _bringToFrontEvent?.Dispose();
        }
        catch { }

        try
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
        catch { }

        try
        {
            _vm.Dispose();
        }
        catch { }

        Close();
        System.Windows.Application.Current?.Shutdown();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExplicitExit && !App.IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
            try
            {
                _trayIcon?.ShowBalloonTip(
                    2000,
                    "XLR Studio",
                    "Still running in the background. Your GoXLR settings stay active.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            catch { }
            return;
        }

        base.OnClosing(e);
    }

    private void Window_Closed(object sender, EventArgs e) =>
        _vm.Dispose();
}
