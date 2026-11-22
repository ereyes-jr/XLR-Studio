using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace ProjectXLR.Hardware;

public sealed record HardwareButtonAndFaderState(
    uint PressedButtonsMask,
    byte FaderA,
    byte FaderB,
    byte FaderC,
    byte FaderD
)
{
    public byte GetFaderVolume(FaderName fader) => fader switch
    {
        FaderName.A => FaderA,
        FaderName.B => FaderB,
        FaderName.C => FaderC,
        FaderName.D => FaderD,
        _ => 255
    };

    public bool IsButtonPressed(int bitIndex) => (PressedButtonsMask & (1u << bitIndex)) != 0;
}

public sealed class GoXlrDeviceController : IDisposable
{
    private readonly object _commandLock = new();

    private uint _cmdHandle;
    private IntPtr _win32Event = IntPtr.Zero;
    private ushort _commandIndex;
    private int _consecutiveErrors;
    private volatile bool _isConnected;

    public bool IsConnected
    {
        get => _isConnected;
        private set => _isConnected = value;
    }
    public bool IsMini { get; private set; } = true;
    public string DeviceModel { get; private set; } = "Disconnected";
    public string SerialNumber { get; private set; } = "";
    public string FirmwareVersion { get; private set; } = "";
    public bool SupportsMix2 { get; private set; }
    public bool Supports1340Colors { get; private set; } = true;

    public event Action<string>? LogMessage;

    public bool Connect(out string error)
    {
        lock (_commandLock)
        {
            Disconnect();
            error = string.Empty;

            try
            {
                // Automatically terminate the legacy official GoXLR App if it is running so it cannot lock the USB handle
                try
                {
                    var legacyProcs = Process.GetProcessesByName("GoXLR App");
                    if (legacyProcs.Length > 0)
                    {
                        foreach (var p in legacyProcs)
                        {
                            try { p.Kill(); p.WaitForExit(1500); } catch { }
                        }
                        Thread.Sleep(350);
                    }
                }
                catch { }

                TUsbAudioInterop.TUSBAUDIO_EnumerateDevices();
                uint count = TUsbAudioInterop.TUSBAUDIO_GetDeviceCount();
                if (count == 0)
                {
                    error = "No GoXLR device detected by TC-Helicon USB driver.";
                    return false;
                }

                uint res = TUsbAudioInterop.TUSBAUDIO_OpenDeviceByIndex(0, out _cmdHandle);
                if (res != 0)
                {
                    error = $"Failed to open GoXLR handle: {TUsbAudioInterop.GetStatusText(res)}";
                    return false;
                }

                if (TUsbAudioInterop.TUSBAUDIO_GetDeviceProperties(_cmdHandle, out var props) == 0)
                {
                    IsMini = props.ProductId == 0x8FE4;
                    DeviceModel = string.IsNullOrWhiteSpace(props.Model)
                        ? (IsMini ? "TC-Helicon GoXLR Mini" : "TC-Helicon GoXLR")
                        : props.Model;
                }

                _win32Event = TUsbAudioInterop.CreateEventW(IntPtr.Zero, false, false, null);
                res = TUsbAudioInterop.TUSBAUDIO_RegisterDeviceNotification(_cmdHandle, uint.MaxValue, _win32Event, 0);
                if (res != 0)
                {
                    error = $"Failed to register device notification: {TUsbAudioInterop.GetStatusText(res)}";
                    Disconnect();
                    return false;
                }

                // 3-step vendor initialization sequence
                DrainNotifications(out _);
                _ = ReadControlRaw(0, 0, 0, 24);
                WriteControlRaw(1, 0, 0, ReadOnlySpan<byte>.Empty);
                WaitForCommandReady(800);
                _ = ReadControlRaw(3, 0, 0, 1040);

                _commandIndex = 0;
                _consecutiveErrors = 0;
                IsConnected = true;

                ReadHardwareMetadata();
                Log($"Connected to {DeviceModel} (SN: {SerialNumber}, FW: {FirmwareVersion})");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Disconnect();
                return false;
            }
        }
    }

    private void ReadHardwareMetadata()
    {
        try
        {
            // Firmware Version (0x80F000)
            byte[] fwResp = PerformRequest(0x0080F000, ReadOnlySpan<byte>.Empty);
            if (fwResp.Length >= 8)
            {
                uint packed = BinaryPrimitives.ReadUInt32LittleEndian(fwResp.AsSpan(0, 4));
                uint build = BinaryPrimitives.ReadUInt32LittleEndian(fwResp.AsSpan(4, 4));
                uint major = packed >> 12;
                uint minor = (packed >> 8) & 0xF;
                uint patch = packed & 0xFF;
                FirmwareVersion = $"{major}.{minor}.{patch}.{build}";

                if (IsMini)
                {
                    SupportsMix2 = (major > 1) || (major == 1 && minor >= 3);
                    Supports1340Colors = (major > 1) || (major == 1 && minor > 1) || (major == 1 && minor == 1 && patch >= 8);
                }
                else
                {
                    SupportsMix2 = (major > 1) || (major == 1 && minor >= 5);
                    Supports1340Colors = (major > 1) || (major == 1 && minor > 3) || (major == 1 && minor == 3 && patch >= 40);
                }
            }

            // Serial Number (0x80F001)
            byte[] snResp = PerformRequest(0x0080F001, ReadOnlySpan<byte>.Empty);
            if (snResp.Length >= 24)
            {
                int len = Array.IndexOf(snResp, (byte)0, 0, 24);
                if (len < 0) len = 24;
                SerialNumber = Encoding.ASCII.GetString(snResp, 0, len);
            }
        }
        catch (Exception ex)
        {
            Log($"Warning reading hardware metadata: {ex.Message}");
        }
    }

    private unsafe bool DrainNotifications(out bool sawInputInterrupt)
    {
        bool sawDataReady = false;
        sawInputInterrupt = false;
        byte* buf = stackalloc byte[1024];

        while (true)
        {
            uint category = 0;
            uint status = TUsbAudioInterop.TUSBAUDIO_ReadDeviceNotification(
                _cmdHandle,
                ref category,
                buf,
                1024,
                out uint bytesRead);

            if (status != 0)
            {
                break;
            }

            if (bytesRead == 6 && buf[0] == 1 && buf[1] == 1)
            {
                if (buf[2] == 1)
                {
                    sawDataReady = true;
                }
                else if (buf[2] == 0)
                {
                    sawInputInterrupt = true;
                }
            }
        }

        return sawDataReady;
    }

    private void WaitForCommandReady(int timeoutMs)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (DrainNotifications(out _))
            {
                return;
            }

            int remaining = (int)Math.Max(1, Math.Min(30, deadline - Environment.TickCount64));
            _ = TUsbAudioInterop.WaitForSingleObject(_win32Event, (uint)remaining);
            if (DrainNotifications(out _))
            {
                return;
            }
        }
    }

    public byte[] PerformRequest(uint commandId, ReadOnlySpan<byte> body, bool isRetry = false, bool isRecoveryReset = false)
    {
        lock (_commandLock)
        {
            if (!IsConnected || _cmdHandle == 0)
            {
                return [];
            }

            try
            {
                if (commandId == 0)
                {
                    _commandIndex = 0;
                }
                else
                {
                    if (_commandIndex == ushort.MaxValue)
                    {
                        _ = PerformRequest(0, ReadOnlySpan<byte>.Empty, isRetry: true, isRecoveryReset: true);
                    }
                    _commandIndex++;
                }

                ushort cmdIdx = _commandIndex;
                byte[] packet = new byte[16 + body.Length];
                BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), commandId);
                BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)body.Length);
                BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), cmdIdx);
                body.CopyTo(packet.AsSpan(16));

                // Clear any pre-existing notifications before issuing our request
                _ = DrainNotifications(out _);

                WriteControlRaw(2, 0, 0, packet);

                // Wait deterministically for [1, 1, 1] (command response ready), ignoring [1, 1, 0] fader interrupts
                WaitForCommandReady(400);

                byte[]? resp = null;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    try
                    {
                        resp = ReadControlRaw(3, 0, 0, 1040);
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == 3)
                            throw new InvalidOperationException($"Command 0x{commandId:X8} (len={body.Length}) failed: {ex.Message}", ex);
                        WaitForCommandReady(40);
                    }
                }
                resp ??= [];
                if (resp.Length < 16)
                {
                    throw new InvalidOperationException($"Invalid GoXLR response length: {resp.Length}");
                }

                ushort respIdx = BinaryPrimitives.ReadUInt16LittleEndian(resp.AsSpan(6, 2));
                if (respIdx != cmdIdx && !isRetry && !isRecoveryReset)
                {
                    _ = PerformRequest(0, ReadOnlySpan<byte>.Empty, isRetry: true, isRecoveryReset: true);
                    return PerformRequest(commandId, body, isRetry: true, isRecoveryReset: false);
                }

                byte[] result = new byte[resp.Length - 16];
                Buffer.BlockCopy(resp, 16, result, 0, result.Length);
                if (!isRecoveryReset)
                {
                    _consecutiveErrors = 0;
                }
                return result;
            }
            catch (Exception ex)
            {
                if (isRecoveryReset)
                {
                    return [];
                }

                _consecutiveErrors++;
                bool handleInvalid = _cmdHandle == 0 || TUsbAudioInterop.TUSBAUDIO_GetDeviceProperties(_cmdHandle, out _) != 0;
                if (handleInvalid || _consecutiveErrors >= 4)
                {
                    Disconnect();
                    Log($"GoXLR USB connection lost: {ex.Message}");
                }
                else
                {
                    Log($"GoXLR USB transient error ({_consecutiveErrors}/4): {ex.Message}");
                    _ = PerformRequest(0, ReadOnlySpan<byte>.Empty, isRetry: true, isRecoveryReset: true);
                }
                return [];
            }
        }
    }

    private unsafe void WriteControlRaw(byte request, ushort value, ushort index, ReadOnlySpan<byte> data)
    {
        byte* scratch = stackalloc byte[64];
        fixed (byte* pData = data)
        {
            uint status = TUsbAudioInterop.TUSBAUDIO_ClassVendorRequestOut(
                _cmdHandle,
                1,
                0,
                request,
                value,
                index,
                (ushort)data.Length,
                pData,
                scratch,
                64);

            if (status != 0)
            {
                throw new InvalidOperationException($"USB VendorRequestOut({request}) failed: {TUsbAudioInterop.GetStatusText(status)}");
            }
        }
    }

    private unsafe byte[] ReadControlRaw(byte request, ushort value, ushort index, ushort maxLength)
    {
        byte[] buffer = new byte[maxLength];
        byte* scratch = stackalloc byte[64];
        fixed (byte* pBuf = buffer)
        {
            uint status = TUsbAudioInterop.TUSBAUDIO_ClassVendorRequestIn(
                _cmdHandle,
                1,
                0,
                request,
                value,
                index,
                maxLength,
                pBuf,
                scratch,
                64);

            if (status != 0)
            {
                throw new InvalidOperationException($"USB VendorRequestIn({request}) failed: {TUsbAudioInterop.GetStatusText(status)}");
            }

            uint actualLen = *(uint*)scratch;
            if (actualLen > maxLength) actualLen = maxLength;
            byte[] output = new byte[actualLen];
            Buffer.BlockCopy(buffer, 0, output, 0, (int)actualLen);
            return output;
        }
    }

    // --- High-level hardware commands ---

    public HardwareButtonAndFaderState? GetButtonStates()
    {
        if (!IsConnected) return null;
        byte[] resp = PerformRequest(0x00800000, ReadOnlySpan<byte>.Empty);
        if (resp.Length < 12) return null;

        uint mask = BinaryPrimitives.ReadUInt32LittleEndian(resp.AsSpan(0, 4));
        return new HardwareButtonAndFaderState(mask, resp[8], resp[9], resp[10], resp[11]);
    }

    public void SetChannelVolume(ChannelName channel, byte volume)
    {
        if (!IsConnected) return;
        uint cmd = (0x806u << 12) | (byte)channel;
        Span<byte> body = [volume];
        _ = PerformRequest(cmd, body);
    }

    public void SetChannelMuted(ChannelName channel, bool muted)
    {
        if (!IsConnected) return;
        uint cmd = (0x809u << 12) | (byte)channel;
        Span<byte> body = [muted ? (byte)1 : (byte)0];
        _ = PerformRequest(cmd, body);
    }

    public void SetFaderChannel(FaderName fader, ChannelName channel)
    {
        if (!IsConnected) return;
        uint cmd = (0x805u << 12) | (byte)fader;
        Span<byte> body = [(byte)channel, 0, 0, 0];
        _ = PerformRequest(cmd, body);
    }

    public void SetFaderDisplayMode(FaderName fader, bool gradient = false, bool meter = false)
    {
        if (!IsConnected) return;
        uint cmd = (0x814u << 12) | (byte)fader;
        Span<byte> body = [gradient ? (byte)1 : (byte)0, meter ? (byte)1 : (byte)0];
        _ = PerformRequest(cmd, body);
    }

    public void SetRoutingForInput(BasicInputDevice input, IReadOnlyDictionary<BasicOutputDevice, bool> routes)
    {
        if (!IsConnected) return;
        int len = SupportsMix2 ? 26 : 22;
        byte[] left = new byte[len];
        byte[] right = new byte[len];

        foreach (var kvp in routes)
        {
            if (!kvp.Value) continue;
            if (kvp.Key == BasicOutputDevice.StreamMix2 && !SupportsMix2) continue;

            var (lPos, rPos) = DspTables.GetRoutingOutputPositions(kvp.Key);
            if (lPos < len && rPos < len)
            {
                left[lPos] = 0x20;
                right[rPos] = 0x20;

                // For mono microphone input, route to BOTH Left and Right channels of stereo destinations
                // (Headphones, BroadcastMix, LineOut) so sidetone and voice are fully audible in both ears!
                if (input == BasicInputDevice.Microphone)
                {
                    left[rPos] = 0x20;
                    right[lPos] = 0x20;
                }
            }
        }

        var (leftId, rightId) = DspTables.GetRoutingInputIds(input);
        _ = PerformRequest((0x804u << 12) | leftId, left);
        _ = PerformRequest((0x804u << 12) | rightId, right);
    }

    public void SetMicrophoneParams(ReadOnlySpan<(MicrophoneParamKey Key, byte B0, byte B1, byte B2, byte B3)> items)
    {
        if (!IsConnected || items.IsEmpty) return;
        byte[] body = new byte[items.Length * 8];
        for (int i = 0; i < items.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(i * 8, 4), (uint)items[i].Key);
            body[i * 8 + 4] = items[i].B0;
            body[i * 8 + 5] = items[i].B1;
            body[i * 8 + 6] = items[i].B2;
            body[i * 8 + 7] = items[i].B3;
        }
        _ = PerformRequest(0x0080B000, body);
    }

    public void SetMicrophoneFloatParams(ReadOnlySpan<(MicrophoneParamKey Key, float Value)> items)
    {
        if (!IsConnected || items.IsEmpty) return;
        byte[] body = new byte[items.Length * 8];
        for (int i = 0; i < items.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(i * 8, 4), (uint)items[i].Key);
            BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(i * 8 + 4, 4), items[i].Value);
        }
        _ = PerformRequest(0x0080B000, body);
    }

    public void SetEffectParams(ReadOnlySpan<(EffectKey Key, int Value)> items)
    {
        if (!IsConnected || items.IsEmpty) return;
        byte[] body = new byte[items.Length * 8];
        for (int i = 0; i < items.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(i * 8, 4), (uint)items[i].Key);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(i * 8 + 4, 4), items[i].Value);
        }
        _ = PerformRequest(0x00801000, body);
    }

    public void SetMicTypeAndGain(MicrophoneType type, ushort gainDb)
    {
        if (!IsConnected) return;
        byte phantom = type == MicrophoneType.Condenser ? (byte)1 : (byte)0;
        MicrophoneParamKey gainKey = type switch
        {
            MicrophoneType.Dynamic => MicrophoneParamKey.DynamicGain,
            MicrophoneType.Condenser => MicrophoneParamKey.CondenserGain,
            _ => MicrophoneParamKey.JackGain
        };
        byte lo = (byte)(gainDb & 0xFF);
        byte hi = (byte)((gainDb >> 8) & 0xFF);

        Span<(MicrophoneParamKey, byte, byte, byte, byte)> payload =
        [
            (MicrophoneParamKey.MicType, phantom, 0, 0, 0),
            (gainKey, 0, 0, lo, hi)
        ];
        SetMicrophoneParams(payload);
    }

    public void SetBleepLevel(int bleepDb)
    {
        if (!IsConnected) return;
        int clamped = Math.Clamp(bleepDb, -36, 0);
        float scaled = (float)clamped * 65536.0f;
        SetMicrophoneFloatParams([(MicrophoneParamKey.BleepLevel, scaled)]);
        SetEffectParams([(EffectKey.BleepLevel, clamped)]);
    }

    public double GetMicrophoneLevelDb()
    {
        if (!IsConnected) return -72.2;
        byte[] resp = PerformRequest(0x0080C000, ReadOnlySpan<byte>.Empty);
        if (resp.Length < 2) return -72.2;
        ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(resp.AsSpan(0, 2));
        if (raw == 0) return -72.2;
        double db = (Math.Log10(raw) * 20.0) - 72.2;
        return Math.Clamp(db, -72.2, 0.0);
    }

    public void SetButtonStates(ReadOnlySpan<byte> states24)
    {
        if (!IsConnected || states24.Length != 24) return;
        _ = PerformRequest(0x00808000, states24);
    }

    public void SetSimpleMiniLighting(byte r, byte g, byte b, ReadOnlySpan<bool> faderMuted = default)
    {
        SetFourZoneMiniLighting((r, g, b), (r, g, b), (r, g, b), (r, g, b), faderMuted);
    }

    public void SetFourZoneMiniLighting(
        (byte R, byte G, byte B) z1,
        (byte R, byte G, byte B) z2,
        (byte R, byte G, byte B) z3,
        (byte R, byte G, byte B) z4,
        ReadOnlySpan<bool> faderMuted = default)
    {
        if (!IsConnected) return;
        int len = Supports1340Colors ? 520 : 328;
        byte[] map = new byte[len];

        static void WriteZone(byte[] buf, int zoneIdx, (byte R, byte G, byte B) c, byte alpha = 0xFF)
        {
            int offset = zoneIdx * 4;
            if (offset + 3 >= buf.Length) return;
            buf[offset] = c.B;
            buf[offset + 1] = c.G;
            buf[offset + 2] = c.R;
            buf[offset + 3] = alpha;
        }

        bool m0 = faderMuted.Length > 0 && faderMuted[0];
        bool m1 = faderMuted.Length > 1 && faderMuted[1];
        bool m2 = faderMuted.Length > 2 && faderMuted[2];
        bool m3 = faderMuted.Length > 3 && faderMuted[3];
        (byte R, byte G, byte B) off = (0, 0, 0);

        // Internal & Logo accent (zones 8..11)
        WriteZone(map, 8, z2); WriteZone(map, 9, z2);
        WriteZone(map, 10, z3); WriteZone(map, 11, z3);

        // Fader 1..4 Mute buttons (zones 12..19):
        // Color0 (even zone) = Active/Dimmed primary color; Color1 (odd zone) = Secondary/Off color
        WriteZone(map, 12, z1); WriteZone(map, 13, off, 0);
        WriteZone(map, 14, z2); WriteZone(map, 15, off, 0);
        WriteZone(map, 16, z3); WriteZone(map, 17, off, 0);
        WriteZone(map, 18, z4); WriteZone(map, 19, off, 0);

        if (Supports1340Colors)
        {
            // Fader 1..4 Meters (start zones 20, 34, 48, 62):
            // Color0 (start+0) = above-slider background (MUST be 0/off so slider position bar works!)
            // Color1 (start+1) = below-slider active fill (off when fader is muted, zN when unmuted!)
            WriteZone(map, 20, off, 0);
            WriteZone(map, 21, m0 ? off : z1, m0 ? (byte)0 : (byte)0xFF);

            WriteZone(map, 34, off, 0);
            WriteZone(map, 35, m1 ? off : z2, m1 ? (byte)0 : (byte)0xFF);

            WriteZone(map, 48, off, 0);
            WriteZone(map, 49, m2 ? off : z3, m2 ? (byte)0 : (byte)0xFF);

            WriteZone(map, 62, off, 0);
            WriteZone(map, 63, m3 ? off : z4, m3 ? (byte)0 : (byte)0xFF);

            // Bleep (126..127) and Cough/MicMute (128..129)
            WriteZone(map, 126, z3); WriteZone(map, 127, off, 0);
            WriteZone(map, 128, z4); WriteZone(map, 129, off, 0);
        }
        else
        {
            WriteZone(map, 20, off, 0); WriteZone(map, 21, m0 ? off : z1, m0 ? (byte)0 : (byte)0xFF);
            WriteZone(map, 22, off, 0); WriteZone(map, 23, m1 ? off : z2, m1 ? (byte)0 : (byte)0xFF);
            WriteZone(map, 24, off, 0); WriteZone(map, 25, m2 ? off : z3, m2 ? (byte)0 : (byte)0xFF);
            WriteZone(map, 26, off, 0); WriteZone(map, 27, m3 ? off : z4, m3 ? (byte)0 : (byte)0xFF);
            WriteZone(map, 78, z3); WriteZone(map, 79, off, 0);
            WriteZone(map, 80, z4); WriteZone(map, 81, off, 0);
        }

        _ = PerformRequest(0x00803000, map);
    }

    /// <summary>
    /// Controls built-in GoXLR hardware LED animation engine (0x00816000 SetAnimationMode).
    /// mode: 0=RetroRainbow, 1=RainbowDark, 2=RainbowBright, 3=Simple(Waterfall), 4=Ripple, 5=None
    /// waterfall: 0=Down, 1=Up, 2=Off
    /// </summary>
    public void SetHardwareAnimationMode(bool enabled, byte mode, byte mod1 = 50, byte mod2 = 50, byte waterfall = 0)
    {
        if (!IsConnected) return;
        Span<byte> payload =
        [
            enabled ? (byte)1 : (byte)0,
            mode,
            Math.Clamp(mod1, (byte)0, (byte)100),
            Math.Clamp(mod2, (byte)0, (byte)100),
            waterfall
        ];
        _ = PerformRequest(0x00816000, payload);
    }

    private void Log(string msg) => LogMessage?.Invoke(msg);

    public void Disconnect()
    {
        lock (_commandLock)
        {
            IsConnected = false;
            _consecutiveErrors = 0;

            if (_win32Event != IntPtr.Zero)
            {
                TUsbAudioInterop.CloseHandle(_win32Event);
                _win32Event = IntPtr.Zero;
            }

            if (_cmdHandle != 0)
            {
                _ = TUsbAudioInterop.TUSBAUDIO_CloseDevice(_cmdHandle);
                _cmdHandle = 0;
            }
        }
    }

    public void Dispose() => Disconnect();
}
