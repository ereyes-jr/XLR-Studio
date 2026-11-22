using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ProjectXLR.Hardware;
using ProjectXLR.Models;
using ProjectXLR.ViewModels;

namespace ProjectXLR;

public partial class App : Application
{
    public static bool IsShuttingDown { get; set; } = false;

    public new void Shutdown(int exitCode = 0)
    {
        IsShuttingDown = true;
        base.Shutdown(exitCode);
    }

    private Mutex? _instanceMutex;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static bool TryBringExistingWindowToFront()
    {
        try
        {
            IntPtr hWnd = FindWindow(null, "XLR Studio");
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, 9); // SW_RESTORE
                SetForegroundWindow(hWnd);
                return true;
            }

            int currentPid = Environment.ProcessId;
            var existingProcesses = System.Diagnostics.Process.GetProcessesByName("ProjectXLR")
                .Where(p => p.Id != currentPid)
                .ToList();

            foreach (var p in existingProcesses)
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(p.MainWindowHandle, 9);
                    SetForegroundWindow(p.MainWindowHandle);
                    return true;
                }
            }

            // No window found; clean up orphaned/headless processes so this launch can show the UI
            foreach (var p in existingProcesses)
            {
                try { p.Kill(); p.WaitForExit(1000); } catch { }
            }
        }
        catch { }
        return false;
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            ProfileManager.LogError("AppDomain.UnhandledException", args.ExceptionObject);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ProfileManager.LogError("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
        DispatcherUnhandledException += (_, args) =>
        {
            ProfileManager.LogError("Dispatcher.UnhandledException", args.Exception);
            args.Handled = true;
        };
        if (e.Args.Contains("--test-visibility", StringComparer.OrdinalIgnoreCase))
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "visibility_test.log");
            try
            {
                WindowsAudioEndpointManager.SetPlaybackChannelVisibleInWindows(ChannelName.Sample, false);
                File.WriteAllText(logPath, "SetPlaybackChannelVisibleInWindows called successfully.");
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, $"ERR: {ex}");
            }
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--test-set-default", StringComparer.OrdinalIgnoreCase))
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "set_default_test.log");
            try
            {
                string? id = WindowsAudioEndpointManager.FindGoXlrSystemRenderEndpointId();
                bool res = WindowsAudioEndpointManager.SetGoXlrSystemAsDefaultPlayback();
                using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var def = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                File.WriteAllText(logPath, $"GoXlrId={id}\nResult={res}\nCurrentDefault={def.FriendlyName} ({def.ID})");
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, $"ERR: {ex}");
            }
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--check-window", StringComparer.OrdinalIgnoreCase))
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "window_check.log");
            try
            {
                var w = new MainWindow();
                File.WriteAllText(logPath, "MainWindow instantiated successfully without any errors!");
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, $"ERR: {ex}");
            }
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--dump-faders", StringComparer.OrdinalIgnoreCase))
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "fader_dump.log");
            try
            {
                using var vm = new MainViewModel();
                var lines = new System.Collections.Generic.List<string>();
                lines.Add($"Device: {vm.DeviceModelText}, FW: {vm.DeviceFirmwareText}, SN: {vm.DeviceSerialText}, Connected={vm.IsConnected}");
                foreach (var slot in vm.FaderSlots)
                {
                    lines.Add($"Slot {slot.Title}: {slot.AssignedChannel} = {slot.VolumePercent}%");
                }
                var hwState = vm.Device.GetButtonStates();
                if (hwState != null)
                {
                    lines.Add($"Parsed: FaderA={hwState.FaderA}, FaderB={hwState.FaderB}, FaderC={hwState.FaderC}, FaderD={hwState.FaderD}");
                }
                File.WriteAllLines(logPath, lines);
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, $"ERR: {ex}");
            }
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--dump-audio", StringComparer.OrdinalIgnoreCase))
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "audio_dump.log");
            try
            {
                using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var lines = new System.Collections.Generic.List<string>();
                lines.Add("=== DEFAULT ENDPOINTS ===");
                try
                {
                    var defRender = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                    lines.Add($"Default Render (Multimedia): {defRender.FriendlyName} ({defRender.ID})");
                }
                catch (Exception ex) { lines.Add($"Default Render (Multimedia) Error: {ex.Message}"); }

                try
                {
                    var defComm = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Communications);
                    lines.Add($"Default Render (Communications): {defComm.FriendlyName} ({defComm.ID})");
                }
                catch (Exception ex) { lines.Add($"Default Render (Communications) Error: {ex.Message}"); }

                try
                {
                    var defCapture = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);
                    lines.Add($"Default Capture (Communications): {defCapture.FriendlyName} ({defCapture.ID})");
                }
                catch (Exception ex) { lines.Add($"Default Capture (Communications) Error: {ex.Message}"); }

                lines.Add("\n=== RENDER ENDPOINTS ===");
                foreach (var d in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active))
                {
                    lines.Add($"RENDER: {d.FriendlyName} | {d.ID}");
                }

                lines.Add("\n=== CAPTURE ENDPOINTS ===");
                foreach (var d in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active))
                {
                    lines.Add($"CAPTURE: {d.FriendlyName} | {d.ID}");
                }

                File.WriteAllLines(logPath, lines);
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, $"ERR: {ex}");
            }
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--test-capture", StringComparer.OrdinalIgnoreCase))
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "capture_test.log");
            var lines = new System.Collections.Generic.List<string>();
            try
            {
                using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var captureDevices = enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active);
                var broadcast = captureDevices.FirstOrDefault(d => d.FriendlyName.Contains("Broadcast"));
                var chatMic = captureDevices.FirstOrDefault(d => d.FriendlyName.Contains("Chat Mic"));

                lines.Add($"Broadcast: {broadcast?.FriendlyName}");
                lines.Add($"ChatMic: {chatMic?.FriendlyName}");

                if (broadcast != null)
                {
                    try
                    {
                        lines.Add("Testing Broadcast with useEventSync = true...");
                        using var c1 = new NAudio.CoreAudioApi.WasapiCapture(broadcast, true, 20);
                        int samples = 0;
                        c1.DataAvailable += (s, ev) => samples += ev.BytesRecorded;
                        c1.StartRecording();
                        System.Threading.Thread.Sleep(500);
                        c1.StopRecording();
                        lines.Add($"Broadcast useEventSync=true OK! Recorded {samples} bytes.");
                    }
                    catch (Exception ex)
                    {
                        lines.Add($"Broadcast useEventSync=true EX: {ex.Message}");
                    }

                    try
                    {
                        lines.Add("Testing Broadcast with useEventSync = false...");
                        using var c2 = new NAudio.CoreAudioApi.WasapiCapture(broadcast, false, 20);
                        int samples = 0;
                        c2.DataAvailable += (s, ev) => samples += ev.BytesRecorded;
                        c2.StartRecording();
                        System.Threading.Thread.Sleep(500);
                        c2.StopRecording();
                        lines.Add($"Broadcast useEventSync=false OK! Recorded {samples} bytes.");
                    }
                    catch (Exception ex)
                    {
                        lines.Add($"Broadcast useEventSync=false EX: {ex.Message}");
                    }
                }

                if (chatMic != null)
                {
                    try
                    {
                        lines.Add("Testing ChatMic with useEventSync = true...");
                        using var c3 = new NAudio.CoreAudioApi.WasapiCapture(chatMic, true, 20);
                        int samples = 0;
                        c3.DataAvailable += (s, ev) => samples += ev.BytesRecorded;
                        c3.StartRecording();
                        System.Threading.Thread.Sleep(500);
                        c3.StopRecording();
                        lines.Add($"ChatMic useEventSync=true OK! Recorded {samples} bytes.");
                    }
                    catch (Exception ex)
                    {
                        lines.Add($"ChatMic useEventSync=true EX: {ex.Message}");
                    }
                }

                File.WriteAllLines(logPath, lines);
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, $"GLOBAL ERR: {ex}");
            }
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "selftest.log");
            try
            {
                using var vm = new MainViewModel(connectSynchronously: true);
                bool initialSidetone = vm.SidetoneEnabled;
                vm.SidetoneEnabled = !initialSidetone;
                bool toggledSidetone = vm.SidetoneEnabled;
                vm.SidetoneEnabled = initialSidetone;
                bool restoredSidetone = vm.SidetoneEnabled;

                // Test Noise Suppression
                bool initialNoiseSuppression = vm.StudioNoiseSuppressionEnabled;
                vm.StudioNoiseSuppressionEnabled = true;
                vm.StudioNoiseSuppressionPreset = "KeyboardRejection";
                string kbPresetLabel = vm.StudioNoiseSuppressionPresetLabel;
                vm.StudioNoiseSuppressionPreset = "StudioClean";
                string studioPresetLabel = vm.StudioNoiseSuppressionPresetLabel;
                vm.StudioNoiseSuppressionEnabled = initialNoiseSuppression;

                // Test Macro Tuning Sliders
                int initialTone = vm.ProfileVocalTonePercent;
                vm.ProfileVocalTonePercent = 85;
                string toneLabel85 = vm.ProfileVocalToneLabel;
                vm.ProfileVocalTonePercent = initialTone;

                int initialRejection = vm.ProfileNoiseRejectionPercent;
                vm.ProfileNoiseRejectionPercent = 80;
                string rejectionLabel80 = vm.ProfileNoiseRejectionLabel;
                vm.ProfileNoiseRejectionPercent = initialRejection;

                // Test Voice Sub-Tabs
                vm.VoiceSubTabIndex = 2;
                bool isHeadphonesSubTab = vm.IsVoiceSubTabHeadphones;
                vm.VoiceSubTabIndex = 0;
                bool isMicSubTab = vm.IsVoiceSubTabMic;

                // Test Headphone Sound Profile & Dynamic Context-Aware Terminology
                vm.SetHeadphonePreset("Gaming");
                string gamingBand0 = vm.HeadphoneBand0Title;
                string gamingBand2 = vm.HeadphoneBand2Title;

                vm.SetHeadphonePreset("Harman");
                string harmanBand0 = vm.HeadphoneBand0Title;
                string harmanBand2 = vm.HeadphoneBand2Title;

                vm.SetHeadphonePreset("BassBoost");
                string bassBand0 = vm.HeadphoneBand0Title;

                vm.SetHeadphonePreset("VocalWarm");
                string vocalBand1 = vm.HeadphoneBand1Title;

                vm.SetHeadphonePreset("Flat");
                string flatBand0 = vm.HeadphoneBand0Title;

                // Restore Harman default
                vm.SetHeadphonePreset("Harman");

                var lines = new[]
                {
                    $"Connected: {vm.IsConnected}",
                    $"ConnectionStatus: {vm.ConnectionStatusText}",
                    $"StatusBanner: {vm.StatusBannerMessage}",
                    $"ProfilesCount: {vm.ProfileNames.Count}",
                    $"Profiles: {string.Join(", ", vm.ProfileNames)}",
                    $"ActiveProfile: {vm.SelectedProfileName}",
                    $"MicType: {vm.MicType}, Gain: {vm.MicGainDb} dB, LiveMicLevel: {vm.MicLevelText}",
                    $"ObsLevelDb: {vm.ObsLevelDb:F1} dB, ObsLevelPercent: {vm.ObsLevelPercent:F1}%, Status: {vm.ObsLevelStatusText}",
                    $"SidetoneToggleTest: initial={initialSidetone}, toggled={toggledSidetone}, restored={restoredSidetone}, status={vm.SidetoneStatusText}",
                    $"NoiseSuppressionTest: active={vm.StudioNoiseSuppressionEnabled}, preset={studioPresetLabel}, kbPreset={kbPresetLabel}",
                    $"MacroTuningTest: tone85={toneLabel85}, rejection80={rejectionLabel80}",
                    $"HeadphonesMaster: Vol={vm.HeadphonesMasterChannel?.VolumePercent}%, Muted={vm.HeadphonesMasterChannel?.IsMuted}",
                    $"LineOutMaster: Vol={vm.LineOutMasterChannel?.VolumePercent}%, Muted={vm.LineOutMasterChannel?.IsMuted}",
                    $"VoiceSubTabsTest: isHeadphones={isHeadphonesSubTab}, isMic={isMicSubTab}",
                    $"HeadphonePresetsTest: GamingB0={gamingBand0}, GamingB2={gamingBand2}, HarmanB0={harmanBand0}, HarmanB2={harmanBand2}, BassB0={bassBand0}, VocalB1={vocalBand1}, FlatB0={flatBand0}",
                    $"PlaybackDevice: {vm.SelectedPlaybackDevice?.DisplayName}",
                    $"GoXlrSystemEndpointId: {WindowsAudioEndpointManager.FindGoXlrSystemRenderEndpointId()}",
                    $"IsGoXlrDefaultWindowsAudio: {vm.IsGoXlrDefaultWindowsAudio}",
                    $"Faders: {string.Join(" | ", vm.FaderSlots.Select(f => $"{f.Title}={f.AssignedChannel}({f.VolumePercent}%, Muted={f.IsMuted}, Enabled={f.IsChannelEnabled})"))}"
                };
                File.WriteAllLines(logPath, lines);
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, $"ERR: {ex}");
            }
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--test-mic-monitor", StringComparer.OrdinalIgnoreCase))
        {
            // Silent check that audio flows from Chat Mic into the headphone output.
            string logPath = Path.Combine(AppContext.BaseDirectory, "mic_monitor_test.log");
            using var engine = new MicMonitorEngine { Volume = 0f };
            string? error = engine.Start();
            Thread.Sleep(3000);
            File.WriteAllText(logPath, $"Error={error ?? "none"}\nRunning={engine.IsRunning}\nFrom={engine.CaptureDeviceName}\nTo={engine.RenderDeviceName}\nCapturedBytes={engine.CapturedBytes}\nPeak={engine.PeakSample}");
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--screenshots", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            UiScreenshotTool.Run(this);
            return;
        }

        if (e.Args.Length == 0)
        {
            _instanceMutex = new Mutex(true, "ProjectXLR_SingleInstance_Mutex_v1", out bool isNewInstance);
            if (!isNewInstance)
            {
                int currentPid = Environment.ProcessId;
                var otherInstances = System.Diagnostics.Process.GetProcessesByName("ProjectXLR")
                    .Where(p => p.Id != currentPid)
                    .ToList();

                if (otherInstances.Count > 0)
                {
                    // Signal running instance to restore and show its window if hidden in tray!
                    try
                    {
                        using var ev = EventWaitHandle.OpenExisting("ProjectXLR_BringToFront_Event");
                        ev.Set();
                        Shutdown(0);
                        return;
                    }
                    catch { }

                    bool broughtToFront = TryBringExistingWindowToFront();
                    if (broughtToFront)
                    {
                        Shutdown(0);
                        return;
                    }
                }

                // If previous instance was a headless zombie or lingering handle, re-acquire mutex and proceed
                try { _instanceMutex = new Mutex(true, "ProjectXLR_SingleInstance_Mutex_v1"); } catch { }
            }
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
