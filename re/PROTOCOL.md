# GoXLR Mini Complete USB & Driver Protocol Specification

This document specifies the exact hardware protocol used to control the TC-Helicon GoXLR and GoXLR Mini (`VID_1220&PID_8FE4`) on Windows via the official TC-Helicon / Thesycon driver (`goxlr_audioapi.dll` / `goxlr_audioapi_x64.dll`), cross-referenced against our static/dynamic analysis of `GoXLR App.exe` v1.6.4.014 and the MIT-licensed `goxlr-utility` reference implementation.

---

## 1. Transport Layer (`goxlr_audioapi_x64.dll`)

### 1.1 Locating the Driver DLL
- Registry lookup: `HKEY_CLASSES_ROOT\CLSID\{024D0372-641F-4B7B-8140-F4DFE458C982}\InprocServer32` (Default value).
- Fallback 64-bit path: `C:\Program Files\TC-Helicon\GoXLR_Audio_Driver\W10_x64\goxlr_audioapi_x64.dll`
- Fallback 32-bit path: `C:\Program Files\TC-Helicon\GoXLR_Audio_Driver\W10_x64\goxlr_audioapi.dll`

### 1.2 Exported Function Signatures (C / stdcall)
```csharp
uint TUSBAUDIO_EnumerateDevices();
uint TUSBAUDIO_GetDeviceCount();
uint TUSBAUDIO_OpenDeviceByIndex(uint index, out uint handle);
uint TUSBAUDIO_CloseDevice(uint handle);
uint TUSBAUDIO_GetDeviceInstanceIdString(uint handle, char* buffer, uint charLen);
uint TUSBAUDIO_GetDeviceProperties(uint handle, ref Properties props);
IntPtr TUSBAUDIO_StatusCodeStringA(uint status);

uint TUSBAUDIO_RegisterDeviceNotification(uint handle, uint filterMask, IntPtr hEvent, uint flags);
uint TUSBAUDIO_ReadDeviceNotification(uint handle, ref uint eventType, byte* buffer, uint bufLen, out uint bytesRead);

uint TUSBAUDIO_ClassVendorRequestOut(
    uint handle,
    uint recipient,       // Always 1 (Interface)
    uint requestType,     // Always 0 (Class/Vendor default)
    uint request,         // 1 = Soft Reset, 2 = DCP Command Out
    uint value,           // Always 0
    ushort index,         // Always 0
    ushort dataLength,    // Payload length in bytes
    byte* data,           // Pointer to buffer
    byte* bytesWritten,   // Pointer to 64-byte scratch buffer (first 4 bytes = u32 written)
    uint bytesWrittenLen  // 64
);

uint TUSBAUDIO_ClassVendorRequestIn(
    uint handle,
    uint recipient,       // Always 1 (Interface)
    uint requestType,     // Always 0
    uint request,         // 0 = Activate Vendor Interface (24 bytes), 3 = DCP Response In (up to 1040 bytes)
    uint value,           // Always 0
    ushort index,         // Always 0
    ushort maxLength,     // 24 for req=0, 1040 for req=3
    byte* dataBuffer,     // Output buffer
    byte* bytesReadBuf,   // 64-byte buffer; first 4 bytes = u32 actual length read
    uint bytesReadBufLen  // 64
);
```

### 1.3 Initialization Handshake & Event Loop
1. Call `TUSBAUDIO_EnumerateDevices()` and `TUSBAUDIO_GetDeviceCount()`, then `TUSBAUDIO_OpenDeviceByIndex(0, out handle)`.
2. Open a second handle (or use the same device ID) on a background event thread:
   - Create an auto-reset Win32 Event (`CreateEvent(null, false, false, null)`).
   - Call `TUSBAUDIO_RegisterDeviceNotification(eventHandle, 0xFFFFFFFF, hEvent, 0)`.
   - Loop `WaitForSingleObject(hEvent, 500)`, then drain `TUSBAUDIO_ReadDeviceNotification` until non-zero (`3992977442` = queue empty).
   - Each notification is **6 bytes**:
     - `[1, 1, 1, _, _, _]` → **Command Response Ready** (unblock pending `read_response(3, ...)`).
     - `[1, 1, 0, _, _, _]` → **Hardware Input Interrupt** (a physical fader moved or button was pressed/released; call `GetButtonStates`).
3. Perform the 3-step USB activation sequence:
   - `ReadResponse(request: 0, value: 0, index: 0, length: 24)` — activates vendor interface & audio streams on Windows.
   - `SendRequest(request: 1, value: 0, index: 0, data: [])` — soft reset.
   - Wait for `[1, 1, 1]` event, then `ReadResponse(request: 3, value: 0, index: 0, length: 1040)` — drain reset response.

---

## 2. DCP Packet Framing (16-Byte Header)

Every command sent via `VendorRequestOut(request = 2)` and read back via `VendorRequestIn(request = 3)` begins with a **16-byte Little-Endian header**:

| Offset | Type | Description |
|---|---|---|
| `0..4` | `u32 LE` | **Command ID** (`(opcode << 12) \| sub_index`) |
| `4..6` | `u16 LE` | **Body Length** in bytes (`N`) |
| `6..8` | `u16 LE` | **Command Sequence Index** (increments by 1 per command; `0` on `ResetCommandIndex`) |
| `8..16` | `u8[8]` | Reserved (`0x00` on send) |
| `16..16+N` | `u8[N]` | **Command / Response Body** |

If the response's `Command Sequence Index` (`bytes[6..8]`) does not match the request's index, send `ResetCommandIndex` (`Command ID = 0`, body `[]`) and retry.

---

## 3. Command Table

| Command | Command ID (`u32`) | Request Body | Response Body |
|---|---|---|---|
| `ResetCommandIndex` | `0x00000000` | Empty | Empty |
| `SystemInfo(SupportsDCP)` | `0x00000001` | `u16 LE` category (`1`=Peaks, `2`=Router, `3`=Mixer, `4`=NVM) | `u16 LE` (`1` = supported) |
| `SystemInfo(Firmware)` | `0x00000002` | Empty | System info bytes |
| `GetButtonStates` | `0x00800000` | Empty | 12+ bytes: `[0..4]` `u32` button bitmask, `[4..8]` `i8[4]` encoders, `[8..12]` `u8[4]` fader volumes (A, B, C, D) |
| `SetEffectParameters` | `0x00801000` | Array of `(u32 key, i32 value)` pairs (8 bytes each) | Empty |
| `SetScribble(fader)` | `(0x802 << 12) \| fader` | `u8[1024]` 128×64 1-bit bitmap (Full GoXLR only) | Empty |
| `SetColourMap` | `0x00803000` | `u8[328]` (old FW) or `u8[520]` (FW ≥ 1.1.8 Mini / 1.3.40 Full) | Empty |
| `SetRouting(input)` | `(0x804 << 12) \| input.id` | `u8[22]` (pre-Mix2) or `u8[26]` (FW ≥ 1.3.0 Mini / 1.5.0 Full) | Empty |
| `SetFader(fader)` | `(0x805 << 12) \| fader` | `[channel_id, 0x00, 0x00, 0x00]` (4 bytes) | Empty |
| `SetChannelVolume(ch)` | `(0x806 << 12) \| ch` | `[volume]` (`u8`, `0..255`) | Empty |
| `SetSubChannelVolume(ch)` | `(0x806 << 12) \| (ch + 16)` | `[volume]` (`u8`, `0..255`) | Empty |
| `SetButtonStates` | `0x00808000` | `u8[24]` (one `ButtonStates` byte per `Buttons` enum index) | Empty |
| `SetChannelState(ch)` | `(0x809 << 12) \| ch` | `[0x00]` = Unmuted, `[0x01]` = Muted | Empty |
| `SetMicrophoneParameters` | `0x0080B000` | Array of `(u32 key, u8[4] value)` pairs (8 bytes each) | Empty |
| `GetMicrophoneLevel` | `0x0080C000` | Empty | `u16 LE` raw level → `dB = clamp(20 * log10(level) - 72.2, -72.2, 0.0)` |
| `GetHardwareInfo(0)` | `0x0080F000` | Empty | 24 bytes: firmware version, build, FPGA count, DICE version |
| `GetHardwareInfo(1)` | `0x0080F001` | Empty | `[0..24]` ASCII serial number, `[24..]` manufacture date |
| `SetFaderDisplayMode(f)` | `(0x814 << 12) \| fader` | `[gradient_bool, meter_bool]` (2 bytes) | Empty |
| `SetAnimationMode` | `0x00816000` | `[enabled, mode, mod1, mod2, waterfall]` (5 bytes) | Empty |
| `SetChannelMixes` | `0x00817000` | `u8[8]` or `u8[10]` Mix A + Mix B output assignment array | Empty |
| `SetMonitoredMix` | `0x00818000` | `[mix]` (`0` = Mix A, `1` = Mix B) | Empty |

---

## 4. Enumerations & IDs

### 4.1 `ChannelName` (0..10) & `FaderName` (0..3)
- `ChannelName`: `Mic = 0`, `LineIn = 1`, `Console = 2`, `System = 3`, `Game = 4`, `Chat = 5`, `Sample = 6`, `Music = 7`, `Headphones = 8`, `MicMonitor = 9`, `LineOut = 10`.
- `FaderName`: `A = 0`, `B = 1`, `C = 2`, `D = 3`.

### 4.2 Routing (`SetRouting`)
Each stereo input (`BasicInputDevice`) has a Left and Right `InputDevice` ID, so changing one route sends **two** `SetRouting` commands (one for Left, one for Right):

| BasicInputDevice | Left ID | Right ID |
|---|---|---|
| `Microphone` | `0x02` | `0x03` |
| `LineIn` | `0x04` | `0x05` |
| `Console` | `0x06` | `0x07` |
| `System` | `0x08` | `0x09` |
| `Game` | `0x0A` | `0x0B` |
| `Chat` | `0x0C` | `0x0D` |
| `Music` | `0x0E` | `0x0F` |
| `Samples` | `0x10` | `0x11` |

Payload is `byte[22]` (or `byte[26]` if `Mix2` supported, i.e. Mini FW ≥ `1.3.0`). To route an input to an output, set `left[LeftPos] = 0x20` and `right[RightPos] = 0x20` (`0x00` = unrouted):
- `Headphones`: Left pos `1`, Right pos `3`
- `BroadcastMix` (Stream Mix 1): Left pos `5`, Right pos `7`
- `ChatMic`: Left pos `9`, Right pos `11`
- `Sampler`: Left pos `13`, Right pos `15`
- `LineOut`: Left pos `17`, Right pos `19`
- `StreamMix2` (if supported): Left pos `21`, Right pos `23`

### 4.3 GoXLR Mini Buttons (`Buttons` bit indices)
- `Fader1Mute = 4`, `Fader2Mute = 9`, `Fader3Mute = 14`, `Fader4Mute = 19`, `Bleep = 22`, `MicrophoneMute (Cough) = 23`.
- `ButtonStates` (LED state per button in 24-byte `SetButtonStates` array):
  - `Colour2 = 0x00`, `Colour1 = 0x01`, `DimmedColour1 = 0x02`, `Flashing = 0x03`, `DimmedColour2 = 0x04`.

---

## 5. Microphone & Filter Parameters (GoXLR Mini)

Important quirk of the GoXLR architecture: **Noise Gate and Compressor parameters must be sent via BOTH `SetMicrophoneParameters` (as `f32 LE`) AND `SetEffectParameters` (as `i32 LE`)**, whereas **GoXLR Mini's 6-band EQ is sent via `SetMicrophoneParameters`**, and **De-Esser / GateEnabled / MicInputMute are sent via `SetEffectParameters`**.

### 5.1 `MicrophoneParamKey` (`0x0080B000` — 4-byte payload per key)
| Parameter | Key (`u32`) | 4-Byte Value Format | Range / Notes |
|---|---|---|---|
| `MicType` | `0x00000` | `[0x01,0,0,0]` if Condenser (+48V), else `[0,0,0,0]` | Dynamic=0, Condenser=1, Jack(3.5mm)=2 |
| `DynamicGain` | `0x00001` | `[0, 0, gain_lo, gain_hi]` (`u16 LE` at offset 2) | `0..72` dB |
| `CondenserGain` | `0x00002` | `[0, 0, gain_lo, gain_hi]` (`u16 LE` at offset 2) | `0..72` dB |
| `JackGain` | `0x00003` | `[0, 0, gain_lo, gain_hi]` (`u16 LE` at offset 2) | `0..72` dB |
| `GateThreshold` | `0x30200` | `f32 LE` | `-59..0` dB |
| `GateAttack` | `0x30400` | `f32 LE` (`GateTimes` enum index `0..45`) | `10ms..2000ms` |
| `GateRelease` | `0x30600` | `f32 LE` (`GateTimes` enum index `0..45`) | `10ms..2000ms` |
| `GateAttenuation` | `0x30900` | `f32 LE` (mapped dB from `GATE_ATTENUATION` table) | `0..100%` → `-6..-61` dB |
| `CompressorThreshold` | `0x60200` | `f32 LE` | `-40..0` dB |
| `CompressorRatio` | `0x60300` | `f32 LE` (**actual ratio float**: `1.0, 1.1, 1.2, 1.4, 1.6, 1.8, 2.0, 2.5, 3.2, 4.0, 5.6, 8.0, 16.0, 32.0, 64.0`) | Note: `EffectKey::CompressorRatio` takes index `0..14`! |
| `CompressorAttack` | `0x60400` | `f32 LE` (`CompressorAttackTime` index `0..19`) | `0.001ms..40ms` |
| `CompressorRelease` | `0x60600` | `f32 LE` (`CompressorReleaseTime` index `0..19`) | `0(15)ms..3000ms` |
| `CompressorMakeUpGain` | `0x60700` | `f32 LE` | `0..24` dB |
| `BleepLevel` | `0x70100` | `f32 LE` (`bleep_db * 65536.0`) | `-36..0` dB |
| `Equalizer90HzFrequency` | `0x40000` | `f32 LE` (Hz) | Default `90.0` (`30..300` Hz) |
| `Equalizer90HzGain` | `0x40001` | `f32 LE` (dB) | `-9..+9` dB |
| `Equalizer250HzFrequency` | `0x40003` | `f32 LE` (Hz) | Default `250.0` (`100..300` Hz) |
| `Equalizer250HzGain` | `0x40004` | `f32 LE` (dB) | `-9..+9` dB |
| `Equalizer500HzFrequency` | `0x40006` | `f32 LE` (Hz) | Default `500.0` (`300..1000` Hz) |
| `Equalizer500HzGain` | `0x40007` | `f32 LE` (dB) | `-9..+9` dB |
| `Equalizer1KHzFrequency` | `0x50000` | `f32 LE` (Hz) | Default `1000.0` (`1000..2000` Hz) |
| `Equalizer1KHzGain` | `0x50001` | `f32 LE` (dB) | `-9..+9` dB |
| `Equalizer3KHzFrequency` | `0x50003` | `f32 LE` (Hz) | Default `3000.0` (`2000..6000` Hz) |
| `Equalizer3KHzGain` | `0x50004` | `f32 LE` (dB) | `-9..+9` dB |
| `Equalizer8KHzFrequency` | `0x50006` | `f32 LE` (Hz) | Default `8000.0` (`6000..18000` Hz) |
| `Equalizer8KHzGain` | `0x50007` | `f32 LE` (dB) | `-9..+9` dB |

*(Note: In Simple 3-band EQ mode on the Mini, Bass controls 90Hz + 250Hz together, Mid controls 500Hz + 1KHz, and Treble controls 3KHz + 8KHz).*

### 5.2 `EffectKey` (`0x00801000` — `i32 LE` value per key)
- `DeEsser = 0x000b` (`0..100` %)
- `GateMode = 0x0010` (`2` default)
- `GateThreshold = 0x0011` (`-59..0` dB)
- `GateEnabled = 0x0014` (`0` or `1`)
- `GateAttenuation = 0x0015` (dB from table `-6..-61`)
- `GateAttack = 0x0016` (index `0..45`)
- `GateRelease = 0x0017` (index `0..45`)
- `BleepLevel = 0x0073` (`-36..0` dB)
- `CompressorRatio = 0x013c` (index `0..14`)
- `CompressorThreshold = 0x013d` (`-40..0` dB)
- `CompressorAttack = 0x013e` (index `0..19`)
- `CompressorRelease = 0x013f` (index `0..19`)
- `CompressorMakeUpGain = 0x0140` (`0..24` dB)
- `MicCompSelect = 0x014b`
- `MicInputMute = 0x0158` (`0` = unmuted, `1` = muted)
