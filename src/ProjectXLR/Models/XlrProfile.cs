using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using ProjectXLR.Hardware;

namespace ProjectXLR.Models;

public sealed class XlrProfile
{
    public string Name { get; set; } = "Default";

    // Fader assignments (A, B, C, D)
    // 1 Mic, 1 Game/System, 1 Chat (Discord), 1 Headphones (Master Output)
    public Dictionary<FaderName, ChannelName> FaderAssignments { get; set; } = new()
    {
        [FaderName.A] = ChannelName.Mic,
        [FaderName.B] = ChannelName.System,
        [FaderName.C] = ChannelName.Chat,
        [FaderName.D] = ChannelName.Headphones
    };

    // Volumes 0..255 for all 11 channels
    public Dictionary<ChannelName, byte> ChannelVolumes { get; set; } = new()
    {
        [ChannelName.Mic] = 255,
        [ChannelName.LineIn] = 255,
        [ChannelName.Console] = 255,
        [ChannelName.System] = 255,
        [ChannelName.Game] = 255,
        [ChannelName.Chat] = 255,
        [ChannelName.Sample] = 255,
        [ChannelName.Music] = 255,
        [ChannelName.Headphones] = 255,
        [ChannelName.MicMonitor] = 200,
        [ChannelName.LineOut] = 255
    };

    public Dictionary<ChannelName, bool> ChannelMuted { get; set; } = Enum.GetValues<ChannelName>()
        .ToDictionary(c => c, _ => false);

    // Disabled I/O channels (empty by default so all audio flows cleanly without OS kernel disable traps)
    public List<BasicInputDevice> DisabledInputs { get; set; } = [];

    public List<BasicOutputDevice> DisabledOutputs { get; set; } = [];

    public bool MicMonitorDisabled { get; set; } = true;
    public string MonitorOutputDeviceId { get; set; } = AudioMonitorService.WindowsDefaultId;

    // Headphone Sound Profile (Audiophile EQ for Sennheiser HD 560S & USB DACs)
    public string HeadphonePreset { get; set; } = "Harman";
    public double HeadphoneSubBassDb { get; set; } = 3.5;
    public double HeadphoneWarmthDb { get; set; } = -0.5;
    public double HeadphonePresenceDb { get; set; } = -1.5;
    public double HeadphoneAirDb { get; set; } = 1.0;

    // Routing matrix: Input -> Output -> Enabled
    public Dictionary<BasicInputDevice, Dictionary<BasicOutputDevice, bool>> Routing { get; set; } = CreateDefaultRouting();

    // Mic & DSP Filter settings
    public MicrophoneType MicType { get; set; } = MicrophoneType.Dynamic;
    public ushort DynamicGainDb { get; set; } = 60;
    public ushort CondenserGainDb { get; set; } = 30;
    public ushort JackGainDb { get; set; } = 30;

    // Noise Gate
    public bool GateEnabled { get; set; } = true;
    public bool GateAdvancedMode { get; set; }
    public int GateSimpleAmount { get; set; } = 25; // 0..100 macro
    public int GateThresholdDb { get; set; } = -47; // -59..0
    public bool VoiceLevelerEnabled { get; set; } = true;
    public bool VoiceCoachEnabled { get; set; } = true;
    public int? VoiceCheckGateDb { get; set; } // The gate "Check my voice" measured for this voice, if any
    public int GateAttenuationPercent { get; set; } = 100; // 0..100
    public int GateAttackIndex { get; set; } = 0; // 0..45 (10ms)
    public int GateReleaseIndex { get; set; } = 19; // 0..45 (200ms)

    // Compressor
    public bool CompressorEnabled { get; set; } = true;
    public bool CompressorAdvancedMode { get; set; }
    public int CompressorSimpleAmount { get; set; } = 50; // 0..100 macro
    public int CompressorThresholdDb { get; set; } = -18; // -40..0
    public int CompressorRatioIndex { get; set; } = 9; // 0..14 (9 = 4.0:1)
    public int CompressorAttackIndex { get; set; } = 1; // 0..19 (2ms)
    public int CompressorReleaseIndex { get; set; } = 9; // 0..19 (100ms)
    public int CompressorMakeUpGainDb { get; set; } = 6; // 0..24

    // De-Esser & Bleep
    public bool DeEsserEnabled { get; set; } = true;
    public int DeEsserPercent { get; set; } = 0; // 0..100
    public int BleepLevelDb { get; set; } = -3; // -36..0 (default -3 dB for loud & clear feedback)

    // Studio Noise Suppression (Discord Krisp Bypass / Hardware DSP Rejection)
    public bool StudioNoiseSuppressionEnabled { get; set; } = true;
    public string StudioNoiseSuppressionPreset { get; set; } = "StudioClean"; // StudioClean, KeyboardRejection, RawStudio

    // Profile Macro Tuning Sliders (0..100)
    public int ProfileVocalTonePercent { get; set; } = 50; // 0 = Crisp Clarity, 50 = Balanced Neutral, 100 = Deep Radio Warmth
    public int ProfileNoiseRejectionPercent { get; set; } = 50; // 0 = Transparent, 50 = Studio Clean, 100 = Aggressive Mechanical Keyboard Rejection

    // 6-Band Mini EQ (-9..+9 dB, plus fine-tune frequencies)
    public bool EqEnabled { get; set; } = true;
    public bool EqAdvancedMode { get; set; }
    public int Eq90HzGain { get; set; } = 5;
    public float Eq90HzFreq { get; set; } = 90f;
    public int Eq250HzGain { get; set; } = 5;
    public float Eq250HzFreq { get; set; } = 250f;
    public int Eq500HzGain { get; set; } = 0;
    public float Eq500HzFreq { get; set; } = 500f;
    public int Eq1KHzGain { get; set; } = 0;
    public float Eq1KHzFreq { get; set; } = 1000f;
    public int Eq3KHzGain { get; set; } = 0;
    public float Eq3KHzFreq { get; set; } = 3000f;
    public int Eq8KHzGain { get; set; } = 0;
    public float Eq8KHzFreq { get; set; } = 8000f;

    // Accent Lighting (RGB) & Hardware Animation Mode
    public string AccentHexColor { get; set; } = "00FFFF";
    public string LightingEffect { get; set; } = "Cyberpunk";
    public int LightingSpeed { get; set; } = 50;
    public string FaderAColor { get; set; } = "00FFFF";
    public string FaderBColor { get; set; } = "0A84FF";
    public string FaderCColor { get; set; } = "A855F7";
    public string FaderDColor { get; set; } = "FF2D55";

    public ushort GetActiveMicGainDb() => MicType switch
    {
        MicrophoneType.Dynamic => DynamicGainDb,
        MicrophoneType.Condenser => CondenserGainDb,
        _ => JackGainDb
    };

    public void SetActiveMicGainDb(ushort db)
    {
        switch (MicType)
        {
            case MicrophoneType.Dynamic: DynamicGainDb = db; break;
            case MicrophoneType.Condenser: CondenserGainDb = db; break;
            default: JackGainDb = db; break;
        }
    }

    public static Dictionary<BasicInputDevice, Dictionary<BasicOutputDevice, bool>> CreateDefaultRouting()
    {
        var map = new Dictionary<BasicInputDevice, Dictionary<BasicOutputDevice, bool>>();
        foreach (var inp in Enum.GetValues<BasicInputDevice>())
        {
            var row = new Dictionary<BasicOutputDevice, bool>();
            foreach (var outp in Enum.GetValues<BasicOutputDevice>())
            {
                bool enabled = outp switch
                {
                    BasicOutputDevice.Headphones => inp != BasicInputDevice.Microphone,
                    // Master headphone mix for external DACs (like AudioQuest DragonFly Red).
                    // System, Game, Chat, and Music route to BroadcastMix so the user can hear them!
                    // Mic to BroadcastMix is controlled dynamically by SidetoneEnabled!
                    BasicOutputDevice.BroadcastMix => inp != BasicInputDevice.Microphone,
                    // Voice Chat Mic MUST only receive Microphone (and optionally soundboard clips)
                    // Desktop sounds, games, music, and voice chat must NEVER leak into ChatMic!
                    BasicOutputDevice.ChatMic => inp == BasicInputDevice.Microphone || inp == BasicInputDevice.Samples,
                    BasicOutputDevice.Sampler => inp != BasicInputDevice.Samples,
                    BasicOutputDevice.LineOut => inp != BasicInputDevice.Microphone,
                    _ => false
                };
                row[outp] = enabled;
            }
            map[inp] = row;
        }
        return map;
    }
}

public sealed class AppSettings
{
    public string ActiveProfileName { get; set; } = "Default";
    public StartupLevelBehavior StartupBehavior { get; set; } = StartupLevelBehavior.SyncPhysicalFaders;
    public bool AutoSaveProfiles { get; set; } = true;
}

public static class ProfileManager
{
    public static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ProjectXLR");

    public static readonly string ProfilesFolder = Path.Combine(AppDataFolder, "Profiles");
    public static readonly string SettingsPath = Path.Combine(AppDataFolder, "settings.json");
    public static readonly string ErrorLogPath = Path.Combine(AppDataFolder, "error.log");

    private static readonly object FileSync = new();

    public static readonly string OfficialGoXlrFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "GoXLR");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PreferredObjectCreationHandling = System.Text.Json.Serialization.JsonObjectCreationHandling.Replace
    };

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataFolder);
        Directory.CreateDirectory(ProfilesFolder);
    }

    public static AppSettings LoadSettings()
    {
        EnsureDirectories();
        if (File.Exists(SettingsPath))
        {
            try
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOpts);
                if (s != null) return s;
            }
            catch
            {
                // Fallback
            }
        }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings)
    {
        if (ProjectXLR.ViewModels.MainViewModel.PreviewMode) return;

        EnsureDirectories();
        WriteAllTextAtomic(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
    }

    private static void WriteAllTextAtomic(string path, string content)
    {
        lock (FileSync)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmpPath = $"{path}.{Environment.ProcessId}.tmp";
            try
            {
                File.WriteAllText(tmpPath, content);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                // Fallback direct write if atomic rename hit transient AV lock
                try { File.WriteAllText(path, content); } catch { }
            }
        }
    }

    public static void LogError(string context, object? error)
    {
        try
        {
            lock (FileSync)
            {
                EnsureDirectories();
                if (File.Exists(ErrorLogPath))
                {
                    var info = new FileInfo(ErrorLogPath);
                    if (info.Length > 256 * 1024)
                    {
                        string tail = File.ReadAllText(ErrorLogPath);
                        if (tail.Length > 128 * 1024)
                        {
                            tail = tail[^ (128 * 1024)..];
                        }
                        File.WriteAllText(ErrorLogPath, tail);
                    }
                }
                File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}] {error}{Environment.NewLine}");
            }
        }
        catch
        {
            // Never throw from diagnostic error logger
        }
    }

    public static List<string> ListProfileNames()
    {
        EnsureDirectories();
        EnsureProProfiles();
        return Directory.GetFiles(ProfilesFolder, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static XlrProfile LoadProfile(string name)
    {
        EnsureDirectories();
        string path = Path.Combine(ProfilesFolder, $"{name}.json");
        if (File.Exists(path))
        {
            try
            {
                var p = JsonSerializer.Deserialize<XlrProfile>(File.ReadAllText(path), JsonOpts);
                if (p != null)
                {
                    p.Name = name;
                    p.DisabledInputs.Remove(BasicInputDevice.Microphone);
                    p.DisabledInputs.Remove(BasicInputDevice.System);
                    p.DisabledOutputs.Remove(BasicOutputDevice.BroadcastMix);
                    p.DisabledOutputs.Remove(BasicOutputDevice.Headphones);
                    p.DisabledOutputs.Remove(BasicOutputDevice.ChatMic);
                    if (p.BleepLevelDb < -20) p.BleepLevelDb = -3;

                    // Fill any missing routing rows/outputs from defaults without overwriting saved user routing
                    var defaultRouting = XlrProfile.CreateDefaultRouting();
                    foreach (var (inp, defRow) in defaultRouting)
                    {
                        if (!p.Routing.TryGetValue(inp, out var row) || row == null)
                        {
                            p.Routing[inp] = new Dictionary<BasicOutputDevice, bool>(defRow);
                        }
                        else
                        {
                            foreach (var (outp, defVal) in defRow)
                            {
                                if (!row.ContainsKey(outp))
                                {
                                    row[outp] = defVal;
                                }
                            }
                        }
                    }

                    // CRITICAL SANITIZATION:
                    // 1. Microphone MUST always route to ChatMic for Discord & voice comms
                    p.Routing[BasicInputDevice.Microphone][BasicOutputDevice.ChatMic] = true;

                    // 2. Prevent hardware feedback loops (Chat -> ChatMic and Samples -> Sampler)
                    p.Routing[BasicInputDevice.Chat][BasicOutputDevice.ChatMic] = false;
                    p.Routing[BasicInputDevice.Samples][BasicOutputDevice.Sampler] = false;

                    return p;
                }
            }
            catch
            {
                // Ignore
            }
        }
        var def = new XlrProfile { Name = name };
        SaveProfile(def);
        return def;
    }

    public static void EnsureProProfiles()
    {
        try
        {
            EnsureDirectories();

            // 1. Streaming Pro
            string streamProPath = Path.Combine(ProfilesFolder, "Streaming Pro.json");
            if (!File.Exists(streamProPath))
            {
                var p = new XlrProfile
                {
                    Name = "Streaming Pro",
                    MicType = MicrophoneType.Dynamic,
                    DynamicGainDb = 60,
                    StudioNoiseSuppressionEnabled = true,
                    StudioNoiseSuppressionPreset = "StudioClean",
                    GateEnabled = true,
                    GateThresholdDb = -42,
                    GateAttenuationPercent = 100,
                    GateAttackIndex = 0,
                    GateReleaseIndex = 19,
                    CompressorEnabled = true,
                    CompressorThresholdDb = -18,
                    CompressorRatioIndex = 8, // 3.2:1
                    CompressorMakeUpGainDb = 4,
                    DeEsserEnabled = true,
                    DeEsserPercent = 15,
                    BleepLevelDb = -3,
                    ProfileVocalTonePercent = 55,
                    ProfileNoiseRejectionPercent = 55,
                    Eq90HzGain = 2,
                    Eq250HzGain = 1,
                    Eq3KHzGain = 2,
                    Eq8KHzGain = 2,
                    AccentHexColor = "00FFFF",
                    LightingEffect = "Cyberpunk"
                };
                SaveProfile(p);
            }

            // 2. Competitive FPS / Comms
            string fpsPath = Path.Combine(ProfilesFolder, "Competitive FPS & Comms.json");
            if (!File.Exists(fpsPath))
            {
                var p = new XlrProfile
                {
                    Name = "Competitive FPS & Comms",
                    MicType = MicrophoneType.Dynamic,
                    DynamicGainDb = 60,
                    StudioNoiseSuppressionEnabled = true,
                    StudioNoiseSuppressionPreset = "KeyboardRejection",
                    GateEnabled = true,
                    GateThresholdDb = -34,
                    GateAttenuationPercent = 100,
                    GateAttackIndex = 0,
                    GateReleaseIndex = 10,
                    CompressorEnabled = true,
                    CompressorThresholdDb = -20,
                    CompressorRatioIndex = 9, // 4.0:1
                    CompressorMakeUpGainDb = 5,
                    DeEsserEnabled = true,
                    DeEsserPercent = 20,
                    BleepLevelDb = 0,
                    ProfileVocalTonePercent = 20,
                    ProfileNoiseRejectionPercent = 85,
                    Eq90HzGain = -2,
                    Eq250HzGain = 0,
                    Eq3KHzGain = 3,
                    Eq8KHzGain = 4,
                    AccentHexColor = "FF9F0A",
                    LightingEffect = "Sunset"
                };
                SaveProfile(p);
            }

            // 3. Podcast Studio Warm
            string podcastPath = Path.Combine(ProfilesFolder, "Podcast Studio Warm.json");
            if (!File.Exists(podcastPath))
            {
                var p = new XlrProfile
                {
                    Name = "Podcast Studio Warm",
                    MicType = MicrophoneType.Dynamic,
                    DynamicGainDb = 62,
                    StudioNoiseSuppressionEnabled = true,
                    StudioNoiseSuppressionPreset = "StudioClean",
                    GateEnabled = true,
                    GateThresholdDb = -46,
                    GateAttenuationPercent = 100,
                    GateAttackIndex = 0,
                    GateReleaseIndex = 22,
                    CompressorEnabled = true,
                    CompressorThresholdDb = -16,
                    CompressorRatioIndex = 7, // 2.5:1
                    CompressorMakeUpGainDb = 4,
                    DeEsserEnabled = true,
                    DeEsserPercent = 15,
                    BleepLevelDb = -3,
                    ProfileVocalTonePercent = 85,
                    ProfileNoiseRejectionPercent = 40,
                    Eq90HzGain = 5,
                    Eq250HzGain = 3,
                    Eq3KHzGain = 1,
                    Eq8KHzGain = 1,
                    AccentHexColor = "A855F7",
                    LightingEffect = "Ocean"
                };
                SaveProfile(p);
            }

            // 4. Late Night Quiet Room
            string lateNightPath = Path.Combine(ProfilesFolder, "Late Night Quiet Room.json");
            if (!File.Exists(lateNightPath))
            {
                var p = new XlrProfile
                {
                    Name = "Late Night Quiet Room",
                    MicType = MicrophoneType.Dynamic,
                    DynamicGainDb = 64,
                    StudioNoiseSuppressionEnabled = true,
                    StudioNoiseSuppressionPreset = "RawStudio",
                    GateEnabled = true,
                    GateThresholdDb = -52,
                    GateAttenuationPercent = 75,
                    GateAttackIndex = 0,
                    GateReleaseIndex = 25,
                    CompressorEnabled = true,
                    CompressorThresholdDb = -24,
                    CompressorRatioIndex = 10, // 5.6:1
                    CompressorMakeUpGainDb = 8,
                    DeEsserEnabled = true,
                    DeEsserPercent = 10,
                    BleepLevelDb = -3,
                    ProfileVocalTonePercent = 60,
                    ProfileNoiseRejectionPercent = 20,
                    Eq90HzGain = 3,
                    Eq250HzGain = 2,
                    Eq3KHzGain = 2,
                    Eq8KHzGain = 2,
                    AccentHexColor = "5E5CE6",
                    LightingEffect = "FireIce"
                };
                SaveProfile(p);
            }

            // 5. Mechanical Keyboard Rejection
            string keyboardPath = Path.Combine(ProfilesFolder, "Mechanical Keyboard Rejection.json");
            if (!File.Exists(keyboardPath))
            {
                var p = new XlrProfile
                {
                    Name = "Mechanical Keyboard Rejection",
                    MicType = MicrophoneType.Dynamic,
                    DynamicGainDb = 58,
                    StudioNoiseSuppressionEnabled = true,
                    StudioNoiseSuppressionPreset = "KeyboardRejection",
                    GateEnabled = true,
                    GateThresholdDb = -32,
                    GateAttenuationPercent = 100,
                    GateAttackIndex = 0,
                    GateReleaseIndex = 8, // Fast 90ms clamp
                    CompressorEnabled = true,
                    CompressorThresholdDb = -18,
                    CompressorRatioIndex = 9, // 4.0:1
                    CompressorMakeUpGainDb = 4,
                    DeEsserEnabled = true,
                    DeEsserPercent = 20,
                    BleepLevelDb = 0,
                    ProfileVocalTonePercent = 35,
                    ProfileNoiseRejectionPercent = 95,
                    Eq90HzGain = -1,
                    Eq250HzGain = 0,
                    Eq3KHzGain = 2,
                    Eq8KHzGain = 3,
                    AccentHexColor = "30D158",
                    LightingEffect = "Matrix"
                };
                SaveProfile(p);
            }

            // 6. Natural / Raw Broadcast
            string naturalPath = Path.Combine(ProfilesFolder, "Natural Raw Broadcast.json");
            if (!File.Exists(naturalPath))
            {
                var p = new XlrProfile
                {
                    Name = "Natural Raw Broadcast",
                    MicType = MicrophoneType.Dynamic,
                    DynamicGainDb = 60,
                    StudioNoiseSuppressionEnabled = false,
                    StudioNoiseSuppressionPreset = "RawStudio",
                    GateEnabled = false,
                    GateThresholdDb = -56,
                    GateAttenuationPercent = 50,
                    CompressorEnabled = false,
                    CompressorThresholdDb = -12,
                    CompressorRatioIndex = 4,
                    CompressorMakeUpGainDb = 0,
                    DeEsserEnabled = false,
                    DeEsserPercent = 0,
                    BleepLevelDb = -3,
                    ProfileVocalTonePercent = 50,
                    ProfileNoiseRejectionPercent = 10,
                    EqEnabled = false,
                    Eq90HzGain = 0,
                    Eq250HzGain = 0,
                    Eq500HzGain = 0,
                    Eq1KHzGain = 0,
                    Eq3KHzGain = 0,
                    Eq8KHzGain = 0,
                    AccentHexColor = "E5E5EA",
                    LightingEffect = "Monochrome"
                };
                SaveProfile(p);
            }
        }
        catch
        {
            // Ignore seeding errors
        }
    }

    public static (string Badge, string Description) GetProfileInfo(string name) => name switch
    {
        "Default" or "Default Broadcast" => 
            ("Twitch & YouTube Live", "Balanced broadcast voice curve with gentle compression, transparent noise gate, and standard stream routing."),
        "Discord Streamer Pro" => 
            ("Discord Voice & FPS Comms", "Studio-clean microphone DSP tuned specifically to bypass Discord Krisp, keeping your voice punchy and natural."),
        "Warm Podcast & Voiceover" => 
            ("Podcasts & Radio Voice", "Ultra-warm broadcast EQ with +5 dB low-end presence, smooth optical leveling, and gentle de-essing."),
        "Late Night Quiet Room" => 
            ("Whispers & Shared Rooms", "High-sensitivity mic gain (+64 dB) with soft-knee gate for quiet late-night gaming without waking roommates."),
        "Mechanical Keyboard Rejection" => 
            ("Clicky Blue/Brown Switches", "Aggressive fast-attack hardware gate that cuts out loud key clacks and mouse clicks between sentences."),
        "Natural Raw Broadcast" => 
            ("Acoustic & Studio Tracking", "Pure uncompressed microphone signal chain with gate and compression bypassed for maximum dynamic realism."),
        _ => ("Custom Preset", "User-configured audio routing, vocal DSP chain, and lighting configuration.")
    };

    public static void SaveProfile(XlrProfile profile)
    {
        if (ProjectXLR.ViewModels.MainViewModel.PreviewMode) return;

        EnsureDirectories();
        string safeName = string.Join("_", profile.Name.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Default";
        profile.Name = safeName;
        string path = Path.Combine(ProfilesFolder, $"{safeName}.json");
        WriteAllTextAtomic(path, JsonSerializer.Serialize(profile, JsonOpts));
    }

    public static void DeleteProfile(string name)
    {
        string path = Path.Combine(ProfilesFolder, $"{name}.json");
        if (File.Exists(path)) File.Delete(path);
    }

    public static int ImportOfficialGoXlrProfiles()
    {
        EnsureDirectories();
        int imported = 0;

        string officialProfilesDir = Path.Combine(OfficialGoXlrFolder, "Profiles");
        string officialMicDir = Path.Combine(OfficialGoXlrFolder, "MicProfiles");

        string? activeMicFile = Path.Combine(officialMicDir, "DEFAULT (2).goxlrMicProfile");
        if (!File.Exists(activeMicFile))
        {
            activeMicFile = Directory.Exists(officialMicDir)
                ? Directory.GetFiles(officialMicDir, "*.goxlrMicProfile").FirstOrDefault()
                : null;
        }

        if (!Directory.Exists(officialProfilesDir)) return 0;

        foreach (string goxlrFile in Directory.GetFiles(officialProfilesDir, "*.goxlr"))
        {
            try
            {
                string profName = Path.GetFileNameWithoutExtension(goxlrFile);
                var profile = ParseOfficialProfile(profName, goxlrFile, activeMicFile);
                SaveProfile(profile);
                imported++;
            }
            catch
            {
                // Skip invalid
            }
        }

        return imported;
    }

    public static XlrProfile ParseOfficialProfile(string name, string goxlrZipPath, string? micProfileXmlPath)
    {
        var result = new XlrProfile { Name = name };

        using (var archive = ZipFile.OpenRead(goxlrZipPath))
        {
            var entry = archive.GetEntry("profile.xml") ?? archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml"));
            if (entry != null)
            {
                using var stream = entry.Open();
                var doc = XDocument.Load(stream);
                var root = doc.Root;
                if (root != null)
                {
                    var mixer = root.Element("mixerTree");
                    if (mixer != null)
                    {
                        ReadVolume(mixer, "micLevel", ChannelName.Mic, result);
                        ReadVolume(mixer, "chatLevel", ChannelName.Chat, result);
                        ReadVolume(mixer, "musicLevel", ChannelName.Music, result);
                        ReadVolume(mixer, "gameLevel", ChannelName.Game, result);
                        ReadVolume(mixer, "consoleLevel", ChannelName.Console, result);
                        ReadVolume(mixer, "lineInLevel", ChannelName.LineIn, result);
                        ReadVolume(mixer, "systemLevel", ChannelName.System, result);
                        ReadVolume(mixer, "sampleLevel", ChannelName.Sample, result);
                        ReadVolume(mixer, "headphoneLevel", ChannelName.Headphones, result);
                        ReadVolume(mixer, "mic2headphoneSubLevel", ChannelName.MicMonitor, result);
                        ReadVolume(mixer, "lineOutLevel", ChannelName.LineOut, result);

                        // Routing
                        var inPrefixes = new (string Prefix, BasicInputDevice Dev)[]
                        {
                            ("mic", BasicInputDevice.Microphone),
                            ("chat", BasicInputDevice.Chat),
                            ("music", BasicInputDevice.Music),
                            ("game", BasicInputDevice.Game),
                            ("console", BasicInputDevice.Console),
                            ("lineIn", BasicInputDevice.LineIn),
                            ("system", BasicInputDevice.System),
                            ("sample", BasicInputDevice.Samples)
                        };
                        var outSuffixes = new (string Suffix, BasicOutputDevice Dev)[]
                        {
                            ("HP", BasicOutputDevice.Headphones),
                            ("Stream", BasicOutputDevice.BroadcastMix),
                            ("Chat", BasicOutputDevice.ChatMic),
                            ("Sampler", BasicOutputDevice.Sampler),
                            ("LineOut", BasicOutputDevice.LineOut)
                        };

                        foreach (var (p, inDev) in inPrefixes)
                        {
                            foreach (var (s, outDev) in outSuffixes)
                            {
                                var attr = mixer.Attribute($"{p}To{s}");
                                if (attr != null && int.TryParse(attr.Value, out int v))
                                {
                                    result.Routing[inDev][outDev] = v > 0;
                                }
                            }
                        }

                        bool sidetoneRouted = result.Routing[BasicInputDevice.Microphone].GetValueOrDefault(BasicOutputDevice.Headphones, true);
                        result.MicMonitorDisabled = !sidetoneRouted;
                        result.ChannelMuted[ChannelName.MicMonitor] = !sidetoneRouted;
                    }

                    ReadFaderAssignment(root.Element("FaderMeter0"), FaderName.A, result);
                    ReadFaderAssignment(root.Element("FaderMeter1"), FaderName.B, result);
                    ReadFaderAssignment(root.Element("FaderMeter2"), FaderName.C, result);
                    ReadFaderAssignment(root.Element("FaderMeter3"), FaderName.D, result);

                    // Default to only having the 4 physical fader channels active
                    result.DisabledInputs.Clear();
                    foreach (var inp in Enum.GetValues<BasicInputDevice>())
                    {
                        ChannelName mappedCh = inp switch
                        {
                            BasicInputDevice.Microphone => ChannelName.Mic,
                            BasicInputDevice.Chat => ChannelName.Chat,
                            BasicInputDevice.Music => ChannelName.Music,
                            BasicInputDevice.Game => ChannelName.Game,
                            BasicInputDevice.Console => ChannelName.Console,
                            BasicInputDevice.LineIn => ChannelName.LineIn,
                            BasicInputDevice.System => ChannelName.System,
                            BasicInputDevice.Samples => ChannelName.Sample,
                            _ => ChannelName.Mic
                        };
                        if (!result.FaderAssignments.Values.Contains(mappedCh))
                        {
                            result.DisabledInputs.Add(inp);
                        }
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(micProfileXmlPath) && File.Exists(micProfileXmlPath))
        {
            try
            {
                var doc = XDocument.Load(micProfileXmlPath);
                var root = doc.Root;
                var dsp = root?.Element("dspTreeMicProfile");
                if (dsp != null)
                {
                    result.GateThresholdDb = ReadIntAttr(dsp, "MIC_GATE_THRESOLD", -47);
                    result.GateAttackIndex = ReadIntAttr(dsp, "MIC_GATE_ATTACK", 0);
                    result.GateReleaseIndex = ReadIntAttr(dsp, "MIC_GATE_RELEASE", 19);
                    result.GateEnabled = ReadIntAttr(dsp, "MIC_GATE_ENABLE", 1) != 0;
                    result.GateAttenuationPercent = ReadIntAttr(dsp, "MIC_GATE_ATTEN", 100);
                    result.GateSimpleAmount = ReadIntAttr(dsp, "MIC_GATE_MACRO_AMOUNT", 25);

                    result.CompressorThresholdDb = ReadIntAttr(dsp, "MIC_COMP_THRESHOLD", -18);
                    result.CompressorRatioIndex = ReadIntAttr(dsp, "MIC_COMP_RATIO", 9);
                    result.CompressorAttackIndex = ReadIntAttr(dsp, "MIC_COMP_ATTACK", 1);
                    result.CompressorReleaseIndex = ReadIntAttr(dsp, "MIC_COMP_RELEASE", 9);
                    result.CompressorMakeUpGainDb = ReadIntAttr(dsp, "MIC_COMP_MAKEUPGAIN", 6);

                    result.DeEsserPercent = ReadIntAttr(dsp, "MIC_DEESS_AMOUNT", 0);

                    result.Eq90HzGain = ReadIntAttr(dsp, "MIC_MINI_EQ_90HZ_GAIN", 0);
                    result.Eq250HzGain = ReadIntAttr(dsp, "MIC_MINI_EQ_250HZ_GAIN", 0);
                    result.Eq500HzGain = ReadIntAttr(dsp, "MIC_MINI_EQ_500HZ_GAIN", 0);
                    result.Eq1KHzGain = ReadIntAttr(dsp, "MIC_MINI_EQ_1KHZ_GAIN", 0);
                    result.Eq3KHzGain = ReadIntAttr(dsp, "MIC_MINI_EQ_3KHZ_GAIN", 0);
                    result.Eq8KHzGain = ReadIntAttr(dsp, "MIC_MINI_EQ_8KHZ_GAIN", 0);
                }

                var setup = root?.Element("setupTreeMicProfile");
                if (setup != null)
                {
                    int mType = ReadIntAttr(setup, "MIC_TYPE", 0);
                    result.MicType = (MicrophoneType)Math.Clamp(mType, 0, 2);
                    result.DynamicGainDb = (ushort)Math.Clamp(ReadIntAttr(setup, "DYNAMIC_MIC_GAIN", 3932160) >> 16, 0, 72);
                    result.CondenserGainDb = (ushort)Math.Clamp(ReadIntAttr(setup, "CONDENSER_MIC_GAIN", 1966080) >> 16, 0, 72);
                    result.JackGainDb = (ushort)Math.Clamp(ReadIntAttr(setup, "TRS_MIC_GAIN", 1966080) >> 16, 0, 72);
                }
            }
            catch
            {
                // Ignore
            }
        }

        return result;
    }

    private static void ReadVolume(XElement mixer, string attrName, ChannelName ch, XlrProfile prof)
    {
        var a = mixer.Attribute(attrName);
        if (a != null && byte.TryParse(a.Value, out byte v))
        {
            prof.ChannelVolumes[ch] = v;
        }
    }

    private static void ReadFaderAssignment(XElement? faderElem, FaderName fader, XlrProfile prof)
    {
        if (faderElem == null) return;
        var attr = faderElem.Attributes().FirstOrDefault(a => a.Name.LocalName.EndsWith("listIndex"));
        if (attr != null && int.TryParse(attr.Value, out int idx))
        {
            ChannelName ch = idx switch
            {
                0 => ChannelName.Mic,
                1 => ChannelName.Chat,
                2 => ChannelName.Music,
                3 => ChannelName.Game,
                4 => ChannelName.Console,
                5 => ChannelName.LineIn,
                6 => ChannelName.System,
                7 => ChannelName.Sample,
                8 => ChannelName.Headphones,
                9 => ChannelName.LineOut,
                _ => ChannelName.Mic
            };
            prof.FaderAssignments[fader] = ch;
        }
    }

    private static int ReadIntAttr(XElement elem, string name, int defaultVal)
    {
        var a = elem.Attribute(name);
        if (a != null && double.TryParse(a.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
        {
            return (int)Math.Round(d);
        }
        return defaultVal;
    }
}
