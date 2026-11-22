using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ProjectXLR.Models;

namespace ProjectXLR.Hardware;

public static class WindowsAudioEndpointManager
{
    private const string MmDevicesBaseKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio";
    private const string EndpointNameValue = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
    private const string DeviceDescValue = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";

    private static readonly Guid ClsidPolicyConfigClient = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
    private static readonly Guid IidPolicyConfigWin10 = new("F8679F50-850A-41CF-9C72-430F290290C8");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetEndpointVisibilityDelegate(
        IntPtr thisPtr,
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
        int bVisible);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetDefaultEndpointDelegate(
        IntPtr thisPtr,
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
        int role);

    /// <summary>
    /// Launches the classic Windows Sound Control Panel (mmsys.cpl) where endpoints can be managed directly.
    /// </summary>
    public static void OpenWindowsSoundControlPanel()
    {
        try
        {
            Process.Start(new ProcessStartInfo("control", "mmsys.cpl sounds") { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>
    /// Sets a playback or capture audio endpoint as the default in Windows (role 0=Console/Multimedia, 1=Multimedia, 2=Communications).
    /// </summary>
    public static bool SetDefaultEndpoint(string fullEndpointId, int role = 0)
    {
        Type? comType = Type.GetTypeFromCLSID(ClsidPolicyConfigClient);
        if (comType == null) return false;

        object? comObj = Activator.CreateInstance(comType);
        if (comObj == null) return false;

        IntPtr unk = Marshal.GetIUnknownForObject(comObj);
        try
        {
            Guid iid = IidPolicyConfigWin10;
            int hr = Marshal.QueryInterface(unk, in iid, out IntPtr ppv);
            if (hr != 0 || ppv == IntPtr.Zero) return false;

            try
            {
                IntPtr vtbl = Marshal.ReadIntPtr(ppv);
                // Index 13 = SetDefaultEndpoint in IPolicyConfig
                IntPtr fnPtr = Marshal.ReadIntPtr(vtbl, 13 * IntPtr.Size);
                var fn = Marshal.GetDelegateForFunctionPointer<SetDefaultEndpointDelegate>(fnPtr);
                int r0 = fn(ppv, fullEndpointId, role);
                if (role == 0)
                {
                    // Also set multimedia role (1)
                    _ = fn(ppv, fullEndpointId, 1);
                }
                return r0 == 0;
            }
            finally
            {
                Marshal.Release(ppv);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.Release(unk);
        }
    }

    /// <summary>
    /// Restores a target audio endpoint (such as an external DAC like AudioQuest DragonFly Red)
    /// as the default Windows playback and communications device when Project XLR is closed.
    /// Invalides lingering GoXLR render streams so active apps (e.g., Chrome, Edge, Spotify, games)
    /// immediately hand off playback to the restored DAC.
    /// </summary>
    public static bool RestoreDeviceAsDefault(string endpointId)
    {
        if (string.IsNullOrWhiteSpace(endpointId) || endpointId == AudioMonitorService.HardwareDirectId)
            return false;

        bool r0 = SetDefaultEndpoint(endpointId, 0); // Console
        bool r1 = SetDefaultEndpoint(endpointId, 1); // Multimedia
        bool r2 = SetDefaultEndpoint(endpointId, 2); // Communications

        // Invalidate active WASAPI streams on GoXLR System and Chat so running applications
        // (e.g. Chrome, Spotify, games, Discord) immediately hand off audio to the restored device
        try
        {
            string? sysId = FindGoXlrSystemRenderEndpointId();
            if (!string.IsNullOrEmpty(sysId))
            {
                InvokeSetEndpointVisibility(sysId, false);
                System.Threading.Thread.Sleep(40);
                InvokeSetEndpointVisibility(sysId, true);
            }

            string? chatId = FindGoXlrChatRenderEndpointId();
            if (!string.IsNullOrEmpty(chatId))
            {
                InvokeSetEndpointVisibility(chatId, false);
                System.Threading.Thread.Sleep(40);
                InvokeSetEndpointVisibility(chatId, true);
            }
        }
        catch { }

        return r0 || r1 || r2;
    }

    /// <summary>
    /// Finds the GoXLR 'System' playback endpoint ID in Windows.
    /// Searches active devices first, then disabled devices (re-enabling if needed).
    /// </summary>
    public static string? FindGoXlrSystemRenderEndpointId()
    {
        try
        {
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            // 1. Search Active first
            foreach (var d in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active))
            {
                if (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                    d.FriendlyName.Contains("System", StringComparison.OrdinalIgnoreCase))
                {
                    return d.ID;
                }
            }

            // 2. Search All (including Disabled) and ensure it's visible
            foreach (var d in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.All))
            {
                if (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                    d.FriendlyName.Contains("System", StringComparison.OrdinalIgnoreCase))
                {
                    SetPlaybackChannelVisibleInWindows(ChannelName.System, true);
                    return d.ID;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Sets GoXLR System as the default Windows multimedia and console playback device.
    /// </summary>
    public static bool SetGoXlrSystemAsDefaultPlayback()
    {
        // Ensure System endpoint is visible first
        SetPlaybackChannelVisibleInWindows(ChannelName.System, true);

        string? id = FindGoXlrSystemRenderEndpointId();
        if (id == null) return false;
        return SetDefaultEndpoint(id, 0);
    }

    /// <summary>
    /// Searches for the GoXLR Chat render (Playback) endpoint ID in Windows.
    /// </summary>
    public static string? FindGoXlrChatRenderEndpointId()
    {
        try
        {
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            foreach (var d in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active))
            {
                if (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                    d.FriendlyName.Contains("Chat", StringComparison.OrdinalIgnoreCase))
                {
                    return d.ID;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Sets GoXLR Chat as the default Windows communications playback device.
    /// </summary>
    public static bool SetGoXlrChatAsDefaultCommunicationsPlayback()
    {
        SetPlaybackChannelVisibleInWindows(ChannelName.Chat, true);
        string? id = FindGoXlrChatRenderEndpointId();
        if (id == null) return false;
        return SetDefaultEndpoint(id, 2); // Role 2 = Communications
    }

    /// <summary>
    /// Searches for the GoXLR Chat Mic capture (Recording) endpoint ID in Windows.
    /// </summary>
    public static string? FindGoXlrChatMicCaptureEndpointId()
    {
        try
        {
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            // 1. Search Active endpoints
            foreach (var d in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active))
            {
                if (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                    d.FriendlyName.Contains("Chat Mic", StringComparison.OrdinalIgnoreCase))
                {
                    return d.ID;
                }
            }

            // 2. Search All (including Disabled) and ensure it's visible
            foreach (var d in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.All))
            {
                if (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                    d.FriendlyName.Contains("Chat Mic", StringComparison.OrdinalIgnoreCase))
                {
                    SetRecordingOutputVisibleInWindows(BasicOutputDevice.ChatMic, true);
                    return d.ID;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Sets GoXLR Chat Mic as the default Windows recording and communication input device.
    /// </summary>
    public static bool SetGoXlrChatMicAsDefaultRecording()
    {
        // Ensure Chat Mic is visible first
        SetRecordingOutputVisibleInWindows(BasicOutputDevice.ChatMic, true);

        string? id = FindGoXlrChatMicCaptureEndpointId();
        if (id == null) return false;

        // Role 0 = Console/Default, Role 1 = Multimedia, Role 2 = Communications
        bool r0 = SetDefaultEndpoint(id, 0);
        bool r1 = SetDefaultEndpoint(id, 1);
        bool r2 = SetDefaultEndpoint(id, 2);
        return r0 || r2;
    }

    /// <summary>
    /// Scans all GoXLR render and capture endpoints in Windows. If any are marked Disabled,
    /// it immediately re-enables them so they are active, functional, and selectable.
    /// </summary>
    public static void EnsureAllGoXlrEndpointsActive()
    {
        try
        {
            using var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            foreach (var (subKeyName, prefixCode) in new[] { ("Render", "{0.0.0.00000000}."), ("Capture", "{0.0.1.00000000}.") })
            {
                using var flowKey = hklm64.OpenSubKey($@"{MmDevicesBaseKey}\{subKeyName}", false);
                if (flowKey == null) continue;

                foreach (string guidSubKey in flowKey.GetSubKeyNames())
                {
                    using var epKey = flowKey.OpenSubKey(guidSubKey, false);
                    if (epKey == null) continue;

                    if (epKey.GetValue("DeviceState") is not int state) continue;
                    int stateNibble = state & 0x0F;
                    // Only care about Disabled endpoints (state == 2)
                    if (stateNibble != 2) continue;

                    using var propsKey = epKey.OpenSubKey("Properties", false);
                    if (propsKey == null) continue;

                    string? devDesc = propsKey.GetValue(DeviceDescValue) as string;
                    bool isGoXlr = (devDesc != null && devDesc.Contains("GoXLR", StringComparison.OrdinalIgnoreCase));
                    if (!isGoXlr)
                    {
                        foreach (string valName in propsKey.GetValueNames())
                        {
                            if (propsKey.GetValue(valName) is string s && s.Contains("GoXLR", StringComparison.OrdinalIgnoreCase))
                            {
                                isGoXlr = true;
                                break;
                            }
                        }
                    }

                    if (isGoXlr)
                    {
                        string fullEndpointId = prefixCode + guidSubKey;
                        InvokeSetEndpointVisibility(fullEndpointId, true);
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Synchronizes the visibility and default assignments of all GoXLR audio endpoints in Windows.
    /// Keeps all GoXLR hardware endpoints active and ensures System, Chat, and Chat Mic defaults.
    /// </summary>
    public static void SyncAllGoXlrEndpoints(XlrProfile profile)
    {
        EnsureAllGoXlrEndpointsActive();
        SetGoXlrChatMicAsDefaultRecording();
    }

    /// <summary>
    /// Ensures a GoXLR virtual playback (Render) channel is active in Windows Sound Settings.
    /// </summary>
    public static void SetPlaybackChannelVisibleInWindows(ChannelName channel, bool visible)
    {
        if (!visible) return; // We intentionally never disable GoXLR endpoints in the Windows OS kernel!
        string? targetName = channel switch
        {
            ChannelName.System => "System",
            ChannelName.Game => "Game",
            ChannelName.Chat => "Chat",
            ChannelName.Music => "Music",
            ChannelName.Sample => "Sample",
            _ => null
        };

        if (targetName == null) return;
        SetGoXlrEndpointVisibility(isCapture: false, targetName, true);
    }

    /// <summary>
    /// Ensures a GoXLR virtual recording (Capture) output is active in Windows Sound Settings.
    /// </summary>
    public static void SetRecordingOutputVisibleInWindows(BasicOutputDevice output, bool visible)
    {
        if (!visible) return; // We intentionally never disable GoXLR endpoints in the Windows OS kernel!
        string? targetName = output switch
        {
            BasicOutputDevice.BroadcastMix => "Broadcast Stream Mix",
            BasicOutputDevice.ChatMic => "Chat Mic",
            BasicOutputDevice.Sampler => "Sample",
            _ => null
        };

        if (targetName == null) return;
        SetGoXlrEndpointVisibility(isCapture: true, targetName, true);
    }

    private static void SetGoXlrEndpointVisibility(bool isCapture, string endpointFriendlyPrefix, bool visible)
    {
        if (!visible) return; // Never disable endpoints in Windows kernel

        try
        {
            string subKeyName = isCapture ? "Capture" : "Render";
            string prefixCode = isCapture ? "{0.0.1.00000000}." : "{0.0.0.00000000}.";

            using var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var flowKey = hklm64.OpenSubKey($@"{MmDevicesBaseKey}\{subKeyName}", false);
            if (flowKey == null) return;

            foreach (string guidSubKey in flowKey.GetSubKeyNames())
            {
                using var epKey = flowKey.OpenSubKey(guidSubKey, false);
                if (epKey == null) continue;

                if (epKey.GetValue("DeviceState") is not int state) continue;
                int stateNibble = state & 0x0F;
                // Only touch endpoints that are physically present (1=Active, 2=Disabled)
                if (stateNibble != 1 && stateNibble != 2)
                    continue;

                using var propsKey = epKey.OpenSubKey("Properties", false);
                if (propsKey == null) continue;

                string? epName = propsKey.GetValue(EndpointNameValue) as string;
                string? devDesc = propsKey.GetValue(DeviceDescValue) as string;

                bool isGoXlr = (devDesc != null && devDesc.Contains("GoXLR", StringComparison.OrdinalIgnoreCase));
                if (!isGoXlr)
                {
                    foreach (string valName in propsKey.GetValueNames())
                    {
                        if (propsKey.GetValue(valName) is string s && s.Contains("GoXLR", StringComparison.OrdinalIgnoreCase))
                        {
                            isGoXlr = true;
                            break;
                        }
                    }
                }

                if (!isGoXlr || string.IsNullOrWhiteSpace(epName))
                    continue;

                if (string.Equals(epName.Trim(), endpointFriendlyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    // If already active, skip
                    if (stateNibble == 1) continue;

                    string fullEndpointId = prefixCode + guidSubKey;
                    InvokeSetEndpointVisibility(fullEndpointId, true);
                }
            }
        }
        catch (Exception ex)
        {
            ProfileManager.LogError("WindowsAudioEndpointManager.SetGoXlrEndpointVisibility", ex);
        }
    }

    private static void InvokeSetEndpointVisibility(string fullEndpointId, bool visible)
    {
        Type? comType = Type.GetTypeFromCLSID(ClsidPolicyConfigClient);
        if (comType == null) return;

        object? comObj = Activator.CreateInstance(comType);
        if (comObj == null) return;

        IntPtr unk = Marshal.GetIUnknownForObject(comObj);
        try
        {
            Guid iid = IidPolicyConfigWin10;
            int hr = Marshal.QueryInterface(unk, in iid, out IntPtr ppv);
            if (hr != 0 || ppv == IntPtr.Zero) return;

            try
            {
                IntPtr vtbl = Marshal.ReadIntPtr(ppv);
                // IPolicyConfig method index 14 = SetEndpointVisibility
                IntPtr fnPtr = Marshal.ReadIntPtr(vtbl, 14 * IntPtr.Size);
                var fn = Marshal.GetDelegateForFunctionPointer<SetEndpointVisibilityDelegate>(fnPtr);
                _ = fn(ppv, fullEndpointId, visible ? 1 : 0);
            }
            finally
            {
                Marshal.Release(ppv);
            }
        }
        finally
        {
            Marshal.Release(unk);
        }
    }
}
