using System;
using System.Collections.Generic;

namespace ProjectXLR.Hardware;

public enum ChannelName : byte
{
    Mic = 0,
    LineIn = 1,
    Console = 2,
    System = 3,
    Game = 4,
    Chat = 5,
    Sample = 6,
    Music = 7,
    Headphones = 8,
    MicMonitor = 9,
    LineOut = 10
}

public enum FaderName : byte
{
    A = 0,
    B = 1,
    C = 2,
    D = 3
}

public enum BasicInputDevice : byte
{
    Microphone = 0,
    Chat = 1,
    Music = 2,
    Game = 3,
    Console = 4,
    LineIn = 5,
    System = 6,
    Samples = 7
}

public enum BasicOutputDevice : byte
{
    Headphones = 0,
    BroadcastMix = 1,
    ChatMic = 2,
    Sampler = 3,
    LineOut = 4,
    StreamMix2 = 5
}

public enum MicrophoneType : byte
{
    Dynamic = 0,
    Condenser = 1,
    Jack = 2
}

public enum MiniEqBand
{
    Eq90Hz,
    Eq250Hz,
    Eq500Hz,
    Eq1KHz,
    Eq3KHz,
    Eq8KHz
}

public enum StartupLevelBehavior
{
    SyncPhysicalFaders,
    Force100Percent,
    UseProfileSavedLevels
}

public enum MicrophoneParamKey : uint
{
    MicType = 0x000,
    DynamicGain = 0x001,
    CondenserGain = 0x002,
    JackGain = 0x003,
    GateThreshold = 0x30200,
    GateAttack = 0x30400,
    GateRelease = 0x30600,
    GateAttenuation = 0x30900,
    CompressorThreshold = 0x60200,
    CompressorRatio = 0x60300,
    CompressorAttack = 0x60400,
    CompressorRelease = 0x60600,
    CompressorMakeUpGain = 0x60700,
    BleepLevel = 0x70100,

    Equalizer90HzFrequency = 0x40000,
    Equalizer90HzGain = 0x40001,
    Equalizer250HzFrequency = 0x40003,
    Equalizer250HzGain = 0x40004,
    Equalizer500HzFrequency = 0x40006,
    Equalizer500HzGain = 0x40007,
    Equalizer1KHzFrequency = 0x50000,
    Equalizer1KHzGain = 0x50001,
    Equalizer3KHzFrequency = 0x50003,
    Equalizer3KHzGain = 0x50004,
    Equalizer8KHzFrequency = 0x50006,
    Equalizer8KHzGain = 0x50007
}

public enum EffectKey : uint
{
    DeEsser = 0x000b,
    GateMode = 0x0010,
    GateThreshold = 0x0011,
    GateEnabled = 0x0014,
    GateAttenuation = 0x0015,
    GateAttack = 0x0016,
    GateRelease = 0x0017,
    BleepLevel = 0x0073,
    CompressorRatio = 0x013c,
    CompressorThreshold = 0x013d,
    CompressorAttack = 0x013e,
    CompressorRelease = 0x013f,
    CompressorMakeUpGain = 0x0140,
    MicCompSelect = 0x014b,
    MicInputMute = 0x0158
}

public static class DspTables
{
    public static readonly float[] CompressorRatios =
    [
        1.0f, 1.1f, 1.2f, 1.4f, 1.6f, 1.8f, 2.0f, 2.5f,
        3.2f, 4.0f, 5.6f, 8.0f, 16.0f, 32.0f, 64.0f
    ];

    public static readonly string[] CompressorRatioLabels =
    [
        "1.0:1", "1.1:1", "1.2:1", "1.4:1", "1.6:1", "1.8:1", "2.0:1", "2.5:1",
        "3.2:1", "4.0:1", "5.6:1", "8.0:1", "16:1", "32:1", "64:1"
    ];

    public static readonly int[] GateTimesMs =
    [
        10, 20, 30, 40, 50, 60, 70, 80, 90, 100,
        110, 120, 130, 140, 150, 160, 170, 180, 190, 200,
        250, 300, 350, 400, 450, 500, 550, 600, 650, 700,
        750, 800, 850, 900, 950, 1000, 1100, 1200, 1300, 1400,
        1500, 1600, 1700, 1800, 1900, 2000
    ];

    public static readonly string[] CompressorAttackLabels =
    [
        "0.001 ms", "2 ms", "3 ms", "4 ms", "5 ms", "6 ms", "7 ms", "8 ms", "9 ms", "10 ms",
        "12 ms", "14 ms", "16 ms", "18 ms", "20 ms", "23 ms", "26 ms", "30 ms", "35 ms", "40 ms"
    ];

    public static readonly string[] CompressorReleaseLabels =
    [
        "0 (15) ms", "15 ms", "25 ms", "35 ms", "45 ms", "55 ms", "65 ms", "75 ms", "85 ms", "100 ms",
        "115 ms", "140 ms", "170 ms", "230 ms", "340 ms", "680 ms", "1000 ms", "1500 ms", "2000 ms", "3000 ms"
    ];

    private static readonly sbyte[] GateAttenuationDb =
    [
        -6, -7, -8, -9, -10, -11, -12, -13, -14, -15,
        -16, -17, -18, -19, -20, -21, -22, -23, -24, -25,
        -26, -27, -28, -30, -32, -61
    ];

    public static sbyte GateAttenuationFromPercent(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        if (clamped > 99) return GateAttenuationDb[25];
        int idx = (int)(clamped * 0.24f);
        return GateAttenuationDb[Math.Clamp(idx, 0, 25)];
    }

    public static (byte LeftId, byte RightId) GetRoutingInputIds(BasicInputDevice input) => input switch
    {
        BasicInputDevice.Microphone => (0x02, 0x03),
        BasicInputDevice.LineIn => (0x04, 0x05),
        BasicInputDevice.Console => (0x06, 0x07),
        BasicInputDevice.System => (0x08, 0x09),
        BasicInputDevice.Game => (0x0A, 0x0B),
        BasicInputDevice.Chat => (0x0C, 0x0D),
        BasicInputDevice.Music => (0x0E, 0x0F),
        BasicInputDevice.Samples => (0x10, 0x11),
        _ => (0x02, 0x03)
    };

    public static (int LeftPos, int RightPos) GetRoutingOutputPositions(BasicOutputDevice output) => output switch
    {
        BasicOutputDevice.Headphones => (1, 3),
        BasicOutputDevice.BroadcastMix => (5, 7),
        BasicOutputDevice.ChatMic => (9, 11),
        BasicOutputDevice.Sampler => (13, 15),
        BasicOutputDevice.LineOut => (17, 19),
        BasicOutputDevice.StreamMix2 => (21, 23),
        _ => (1, 3)
    };

    public static string GetChannelDisplayName(ChannelName ch) => ch switch
    {
        ChannelName.Mic => "Microphone",
        ChannelName.LineIn => "Line In",
        ChannelName.Console => "Console",
        ChannelName.System => "System",
        ChannelName.Game => "Game",
        ChannelName.Chat => "Voice Chat",
        ChannelName.Sample => "Samples",
        ChannelName.Music => "Music",
        ChannelName.Headphones => "Headphones",
        ChannelName.MicMonitor => "Mic Monitor",
        ChannelName.LineOut => "Line Out",
        _ => ch.ToString()
    };

    public static string GetInputDisplayName(BasicInputDevice input) => input switch
    {
        BasicInputDevice.Microphone => "Microphone",
        BasicInputDevice.Chat => "Voice Chat",
        BasicInputDevice.Music => "Music",
        BasicInputDevice.Game => "Game",
        BasicInputDevice.Console => "Console",
        BasicInputDevice.LineIn => "Line In",
        BasicInputDevice.System => "System",
        BasicInputDevice.Samples => "Samples",
        _ => input.ToString()
    };

    public static string GetOutputDisplayName(BasicOutputDevice output) => output switch
    {
        BasicOutputDevice.Headphones => "Headphones",
        BasicOutputDevice.BroadcastMix => "Broadcast Stream Mix",
        BasicOutputDevice.ChatMic => "Chat Mic",
        BasicOutputDevice.Sampler => "Sampler",
        BasicOutputDevice.LineOut => "Line Out",
        BasicOutputDevice.StreamMix2 => "Stream Mix 2 / VOD",
        _ => output.ToString()
    };
}
