using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ProjectXLR.Hardware;

public enum DeepFilterNetStatus
{
    NotInstalled,
    NeedsHook,
    PluginMissing,
    Paused,
    Active
}

public sealed class DeepFilterNetService
{
    public const string EqualizerApoDir = @"C:\Program Files\EqualizerAPO";
    public const string EqualizerApoConfigDir = @"C:\Program Files\EqualizerAPO\config";
    public const string EqualizerApoConfigFile = @"C:\Program Files\EqualizerAPO\config\config.txt";
    public const string EqualizerApoVstDir = @"C:\Program Files\EqualizerAPO\VSTPlugins";
    public const string VstPluginInstalledFile = @"C:\Program Files\EqualizerAPO\VSTPlugins\Alt Denoiser.dll";
    public const string EqualizerApoClsid = "{EC1CC9CE-FAED-4822-828A-82A81A6F018F}";

    private const string EndpointNameValue = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
    private const string DeviceDescValue = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";

    public DeepFilterNetStatus CurrentStatus { get; private set; } = DeepFilterNetStatus.NotInstalled;
    public string StatusTitle { get; private set; } = "DeepFilterNet 3 AI Status Unknown";
    public string StatusMessage { get; private set; } = "";
    public string StatusBadgeColor { get; private set; } = "#8E8E98";
    public bool IsActive => CurrentStatus == DeepFilterNetStatus.Active;
    public bool IsInstalled => File.Exists(Path.Combine(EqualizerApoDir, "Configurator.exe")) || File.Exists(Path.Combine(EqualizerApoDir, "EqualizerAPO.dll"));
    public bool IsPluginInstalled => File.Exists(VstPluginInstalledFile);
    public bool IsChatMicHooked { get; private set; }
    public bool IsEnabledInConfig { get; private set; }

    public event Action? StatusChanged;

    public DeepFilterNetService()
    {
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        bool installed = IsInstalled;
        bool pluginReady = IsPluginInstalled;
        bool hooked = CheckIfChatMicHooked();
        IsChatMicHooked = hooked;

        bool activeInConfig = false;
        if (File.Exists(EqualizerApoConfigFile))
        {
            try
            {
                var lines = File.ReadAllLines(EqualizerApoConfigFile);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("VSTPlugin:", StringComparison.OrdinalIgnoreCase) &&
                        trimmed.Contains("Alt Denoiser", StringComparison.OrdinalIgnoreCase))
                    {
                        activeInConfig = true;
                        break;
                    }
                }
            }
            catch { }
        }
        IsEnabledInConfig = activeInConfig;

        if (!installed)
        {
            CurrentStatus = DeepFilterNetStatus.NotInstalled;
            StatusTitle = "DeepFilterNet 3 AI: Not Installed";
            StatusMessage = "Install Equalizer APO + DeepFilterNet 3 to eliminate mechanical keyboard clatter in Discord Studio Mode and games with 0 extra audio devices.";
            StatusBadgeColor = "#8E8E98"; // Gray
        }
        else if (!hooked)
        {
            CurrentStatus = DeepFilterNetStatus.NeedsHook;
            StatusTitle = "DeepFilterNet 3 AI: Chat Mic Needs Device Hook";
            StatusMessage = "Equalizer APO is installed, but 'Chat Mic' is not checked in the Configurator. Click 'Hook Chat Mic' to activate.";
            StatusBadgeColor = "#FF9F0A"; // Orange
        }
        else if (!pluginReady)
        {
            CurrentStatus = DeepFilterNetStatus.PluginMissing;
            StatusTitle = "DeepFilterNet 3 AI: Plugin File Missing";
            StatusMessage = "DeepFilterNet 3 VST was not found in Equalizer APO's plugin folder. Click 'Repair Configuration' to restore it.";
            StatusBadgeColor = "#FF453A"; // Red
        }
        else if (!activeInConfig)
        {
            CurrentStatus = DeepFilterNetStatus.Paused;
            StatusTitle = "DeepFilterNet 3 AI: Paused (Bypassed)";
            StatusMessage = "DeepFilterNet 3 is configured on Chat Mic but paused. Toggle ON to resume neural AI noise removal.";
            StatusBadgeColor = "#FF9F0A"; // Orange
        }
        else
        {
            CurrentStatus = DeepFilterNetStatus.Active;
            StatusTitle = "DeepFilterNet 3 AI: Active (Zero Noise)";
            StatusMessage = "DeepFilterNet 3 is actively running at the driver level on Chat Mic. Mechanical keyboard clicks are eliminated across Discord (Studio Mode), games, and all voice apps with 0 extra devices.";
            StatusBadgeColor = "#30D158"; // Green
        }

        StatusChanged?.Invoke();
    }

    private static bool CheckIfChatMicHooked()
    {
        try
        {
            using var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var captureKey = hklm64.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture", false);
            if (captureKey == null) return false;

            foreach (var guid in captureKey.GetSubKeyNames())
            {
                using var epKey = captureKey.OpenSubKey(guid, false);
                if (epKey == null) continue;

                using var propsKey = epKey.OpenSubKey("Properties", false);
                if (propsKey == null) continue;

                string? epName = propsKey.GetValue(EndpointNameValue) as string;
                string? devDesc = propsKey.GetValue(DeviceDescValue) as string;

                bool isChatMic = (epName != null && epName.Contains("Chat Mic", StringComparison.OrdinalIgnoreCase)) ||
                                 (devDesc != null && devDesc.Contains("Chat Mic", StringComparison.OrdinalIgnoreCase));

                if (!isChatMic)
                {
                    foreach (var valName in propsKey.GetValueNames())
                    {
                        if (propsKey.GetValue(valName) is string s && s.Contains("Chat Mic", StringComparison.OrdinalIgnoreCase))
                        {
                            isChatMic = true;
                            break;
                        }
                    }
                }

                if (isChatMic)
                {
                    // Check FxProperties for Equalizer APO CLSID
                    using var fxKey = epKey.OpenSubKey("FxProperties", false);
                    if (fxKey != null)
                    {
                        foreach (var valName in fxKey.GetValueNames())
                        {
                            if (fxKey.GetValue(valName) is string val && val.Contains(EqualizerApoClsid, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // Fallback: check config.txt presence
        if (File.Exists(EqualizerApoConfigFile))
        {
            try
            {
                var text = File.ReadAllText(EqualizerApoConfigFile);
                if (text.Contains("Chat Mic", StringComparison.OrdinalIgnoreCase) && text.Contains("Alt Denoiser", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    public const string ChunkGentle = "VkMyIWQAAAA8P3htbCB2ZXJzaW9uPSIxLjAiIGVuY29kaW5nPSJVVEYtOCI/PiA8UGFyYW1ldGVycz48UEFSQU0gaWQ9ImF0dGVuX2xpbSIgdmFsdWU9IjIwLjAiLz48L1BhcmFtZXRlcnM+AA==";
    public const string ChunkBalanced = "VkMyIWQAAAA8P3htbCB2ZXJzaW9uPSIxLjAiIGVuY29kaW5nPSJVVEYtOCI/PiA8UGFyYW1ldGVycz48UEFSQU0gaWQ9ImF0dGVuX2xpbSIgdmFsdWU9IjMwLjUiLz48L1BhcmFtZXRlcnM+AA==";
    public const string ChunkAggressive = "VkMyIWUAAAA8P3htbCB2ZXJzaW9uPSIxLjAiIGVuY29kaW5nPSJVVEYtOCI/PiA8UGFyYW1ldGVycz48UEFSQU0gaWQ9ImF0dGVuX2xpbSIgdmFsdWU9IjEwMC4wIi8+PC9QYXJhbWV0ZXJzPgA=";

    public void ToggleEnabled(bool enable)
    {
        if (!File.Exists(EqualizerApoConfigFile))
        {
            EnsureConfigFile(enabled: enable);
            RefreshStatus();
            return;
        }

        try
        {
            var lines = File.ReadAllLines(EqualizerApoConfigFile).ToList();
            bool hasVst = false;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.Contains("Alt Denoiser", StringComparison.OrdinalIgnoreCase))
                {
                    hasVst = true;
                    if (enable && line.StartsWith("#"))
                    {
                        lines[i] = line.TrimStart('#', ' ');
                    }
                    else if (!enable && !line.StartsWith("#"))
                    {
                        lines[i] = "# " + line;
                    }
                }
                else if (line.StartsWith("Device:", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "Device: Chat Mic";
                }
            }

            if (!hasVst && enable)
            {
                EnsureConfigFile(enabled: true);
            }
            else
            {
                File.WriteAllLines(EqualizerApoConfigFile, lines);
            }
        }
        catch { }

        RefreshStatus();
    }

    public void EnsureConfigFile(string level = "Balanced", bool enabled = true)
    {
        string chunk = level switch
        {
            "Gentle" => ChunkGentle,
            "Aggressive" => ChunkAggressive,
            _ => ChunkBalanced
        };

        string prefix = enabled ? "" : "# ";
        var lines = new[]
        {
            "# Project XLR - DeepFilterNet 3 Neural AI Noise Suppression",
            "# Eliminates mechanical keyboard clatter for Discord Studio Mode & games",
            "Device: Chat Mic",
            $"{prefix}VSTPlugin: Library \"{VstPluginInstalledFile}\" ChunkData \"{chunk}\""
        };

        try
        {
            string dir = Path.GetDirectoryName(EqualizerApoConfigFile)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(EqualizerApoConfigFile, lines);
        }
        catch { }
    }

    public static string? FindToolPath(string relativePath)
    {
        string[] candidates = [
            Path.Combine(AppContext.BaseDirectory, relativePath),
            Path.Combine(AppContext.BaseDirectory, "tools", relativePath),
            Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\tools", relativePath),
            Path.Combine(AppContext.BaseDirectory, @"..\..\..", relativePath)
        ];

        foreach (var p in candidates)
        {
            try
            {
                string full = Path.GetFullPath(p);
                if (File.Exists(full) || Directory.Exists(full))
                {
                    return full;
                }
            }
            catch { }
        }
        return null;
    }

    public async Task<bool> InstallOrRepairAsync()
    {
        string? installerPath = FindToolPath(@"EqualizerAPO\EqualizerAPO-x64-1.4.2.exe");
        string? pluginDllSource = FindToolPath(@"Alt-Denoiser\Alt-Denoiser-windows\VST\Alt Denoiser.dll");
        string? pluginVst3Source = FindToolPath(@"Alt-Denoiser\Alt-Denoiser-windows\VST3\Alt Denoiser.vst3");

        // Create temporary elevated powershell helper script
        string tempScript = Path.Combine(Path.GetTempPath(), "setup_deepfilternet.ps1");
        string scriptContent = $@"
# DeepFilterNet 3 Auto-Deployer for Project XLR
$ErrorActionPreference = 'SilentlyContinue'

# 1. Run installer if Configurator is missing
if (-not (Test-Path '{EqualizerApoDir}\Configurator.exe')) {{
    if (Test-Path '{installerPath}') {{
        Start-Process -FilePath '{installerPath}' -Wait
    }}
}}

# 2. Ensure VST directory
if (-not (Test-Path '{EqualizerApoVstDir}')) {{
    New-Item -ItemType Directory -Force -Path '{EqualizerApoVstDir}' | Out-Null
}}

# 3. Copy plugin DLL
if (Test-Path '{pluginDllSource}') {{
    Copy-Item -Path '{pluginDllSource}' -Destination '{VstPluginInstalledFile}' -Force
}}

# 4. Copy VST3 to Common Files if available
if (Test-Path '{pluginVst3Source}') {{
    $vst3Common = 'C:\Program Files\Common Files\VST3'
    if (-not (Test-Path $vst3Common)) {{ New-Item -ItemType Directory -Force -Path $vst3Common | Out-Null }}
    Copy-Item -Path '{pluginVst3Source}' -Destination ""$vst3Common\Alt Denoiser.vst3"" -Recurse -Force
}}

# 5. Write config.txt
if (-not (Test-Path '{EqualizerApoConfigDir}')) {{
    New-Item -ItemType Directory -Force -Path '{EqualizerApoConfigDir}' | Out-Null
}}

$config = @(
    '# Project XLR - DeepFilterNet 3 Neural AI Noise Suppression',
    '# Eliminates mechanical keyboard clatter for Discord Studio Mode & games',
    'Device: Chat Mic',
    'VSTPlugin: Library ""{VstPluginInstalledFile}"" ChunkData ""{ChunkBalanced}""'
)
$config | Out-File -FilePath '{EqualizerApoConfigFile}' -Encoding utf8 -Force

# 6. Launch Configurator if Chat Mic is not hooked
if (Test-Path '{EqualizerApoDir}\Configurator.exe') {{
    Start-Process -FilePath '{EqualizerApoDir}\Configurator.exe'
}}
";

        try
        {
            await File.WriteAllTextAsync(tempScript, scriptContent);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScript}\"",
                UseShellExecute = true,
                Verb = "runas" // Prompt UAC elevation
            };

            var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
            }

            await Task.Delay(1000);
            RefreshStatus();
            return true;
        }
        catch (Exception)
        {
            RefreshStatus();
            return false;
        }
        finally
        {
            try { if (File.Exists(tempScript)) File.Delete(tempScript); } catch { }
        }
    }

    public void OpenConfigurator()
    {
        string cfgPath = Path.Combine(EqualizerApoDir, "Configurator.exe");
        if (File.Exists(cfgPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo(cfgPath) { UseShellExecute = true, Verb = "runas" });
            }
            catch { }
        }
    }

    public void OpenConfigFolder()
    {
        if (Directory.Exists(EqualizerApoConfigDir))
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", EqualizerApoConfigDir) { UseShellExecute = true });
            }
            catch { }
        }
    }

    /// <summary>The strength actually in the Equalizer APO config, so the UI never claims a different one.</summary>
    public string ReadDenoiseStrength()
    {
        try
        {
            if (File.Exists(EqualizerApoConfigFile))
            {
                string config = File.ReadAllText(EqualizerApoConfigFile);
                if (config.Contains(ChunkGentle, StringComparison.Ordinal)) return "Gentle";
                if (config.Contains(ChunkAggressive, StringComparison.Ordinal)) return "Aggressive";
            }
        }
        catch { }
        return "Balanced";
    }

    public void SetDenoiseStrength(string level)
    {
        string chunk = level switch
        {
            "Gentle" => ChunkGentle,
            "Aggressive" => ChunkAggressive,
            _ => ChunkBalanced
        };

        if (!File.Exists(EqualizerApoConfigFile))
        {
            EnsureConfigFile(level, enabled: true);
            RefreshStatus();
            return;
        }

        try
        {
            var lines = File.ReadAllLines(EqualizerApoConfigFile).ToList();
            bool hasVst = false;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line.Contains("Alt Denoiser", StringComparison.OrdinalIgnoreCase))
                {
                    bool isCommented = line.Trim().StartsWith("#");
                    string baseCmd = $"VSTPlugin: Library \"{VstPluginInstalledFile}\" ChunkData \"{chunk}\"";
                    lines[i] = isCommented ? "# " + baseCmd : baseCmd;
                    hasVst = true;
                }
                else if (line.Trim().StartsWith("Device:", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "Device: Chat Mic";
                }
            }

            if (!hasVst)
            {
                EnsureConfigFile(level, enabled: true);
            }
            else
            {
                File.WriteAllLines(EqualizerApoConfigFile, lines);
            }
        }
        catch { }

        RefreshStatus();
    }

    public void LaunchStandaloneVisualizer()
    {
        string? exe = FindToolPath(@"Alt-Denoiser\Alt-Denoiser-windows\Standalone\Alt Denoiser.exe");
        if (exe != null && File.Exists(exe))
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
