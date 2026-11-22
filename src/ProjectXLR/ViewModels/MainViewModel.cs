using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ProjectXLR.Hardware;
using ProjectXLR.Models;

namespace ProjectXLR.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class ProfileChipViewModel : ObservableObject
{
    public string Name { get; }
    public bool IsActive { get; }
    public string DisplayLabel => IsActive ? $"{Name}  •  Active" : Name;

    public ProfileChipViewModel(string name, bool isActive)
    {
        Name = name;
        IsActive = isActive;
    }
}

public sealed class ProfileCardViewModel : ObservableObject
{
    private bool _isActive;
    public string Name { get; }
    public string TargetBadge { get; }
    public string Description { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public ProfileCardViewModel(string name, string targetBadge, string description, bool isActive)
    {
        Name = name;
        TargetBadge = targetBadge;
        Description = description;
        _isActive = isActive;
    }
}

public sealed class ChannelMixerItemViewModel : ObservableObject
{
    private readonly Action<ChannelMixerItemViewModel> _onVolumeChanged;
    private readonly Action<ChannelMixerItemViewModel> _onMuteChanged;
    private readonly Action<ChannelMixerItemViewModel> _onEnabledChanged;
    private byte _volume = 255;
    private bool _isMuted;
    private bool _isDisabled;
    private string _assignedFaderBadge = "";
    private bool _isAssignedToPhysicalFader;
    private bool _suppressCallback;

    public ChannelName Channel { get; }
    public string DisplayName => DspTables.GetChannelDisplayName(Channel);
    public string IconGlyph => Channel switch
    {
        ChannelName.Mic => "🎙️",
        ChannelName.Chat => "💬",
        ChannelName.Music => "🎵",
        ChannelName.System => "💻",
        ChannelName.Game => "🎮",
        ChannelName.Console => "📻",
        ChannelName.LineIn => "🔌",
        ChannelName.Sample => "🔔",
        ChannelName.Headphones => "🎧",
        ChannelName.MicMonitor => "👂",
        ChannelName.LineOut => "🔊",
        _ => "🎚️"
    };

    public string FormattedDbLevel
    {
        get
        {
            if (_volume == 0) return "-∞ dB";
            double db = 20.0 * Math.Log10(_volume / 255.0);
            return $"{db:F1} dB";
        }
    }

    public string ChannelSubtitle => Channel switch
    {
        ChannelName.Mic => "XLR / 3.5mm Microphone Input",
        ChannelName.Chat => "Windows Voice Chat (Discord, TeamSpeak)",
        ChannelName.Music => "Windows Music (Spotify, Browser)",
        ChannelName.System => "Windows Default Audio Playback",
        ChannelName.Game => "Windows Game Audio Playback",
        ChannelName.Console => "Optical / Console Audio Input",
        ChannelName.LineIn => "3.5mm Stereo Line-In Port",
        ChannelName.Sample => "Windows Sampler Playback",
        ChannelName.Headphones => "3.5mm Headphone Output Master",
        ChannelName.MicMonitor => "Zero-Latency Sidetone (Hear Yourself)",
        ChannelName.LineOut => "3.5mm Line-Out Speaker Master",
        _ => ""
    };

    public ChannelMixerItemViewModel(
        ChannelName channel,
        Action<ChannelMixerItemViewModel> onVolumeChanged,
        Action<ChannelMixerItemViewModel> onMuteChanged,
        Action<ChannelMixerItemViewModel> onEnabledChanged)
    {
        Channel = channel;
        _onVolumeChanged = onVolumeChanged;
        _onMuteChanged = onMuteChanged;
        _onEnabledChanged = onEnabledChanged;
    }

    public int VolumePercent
    {
        get => (int)Math.Round(_volume / 255.0 * 100.0);
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            byte raw = (byte)Math.Round(clamped / 100.0 * 255.0);
            if (_volume != raw)
            {
                _volume = raw;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumeRaw));
                if (!_suppressCallback) _onVolumeChanged(this);
            }
        }
    }

    public byte VolumeRaw
    {
        get => _volume;
        set
        {
            if (_volume != value)
            {
                _volume = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumePercent));
                if (!_suppressCallback) _onVolumeChanged(this);
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value) && !_suppressCallback)
            {
                _onMuteChanged(this);
            }
        }
    }

    public bool IsDisabled
    {
        get => _isDisabled;
        set
        {
            if (SetProperty(ref _isDisabled, value))
            {
                OnPropertyChanged(nameof(IsChannelEnabled));
            }
        }
    }

    public bool IsChannelEnabled
    {
        get => !_isDisabled;
        set
        {
            bool newDisabled = !value;
            if (SetProperty(ref _isDisabled, newDisabled, nameof(IsDisabled)))
            {
                OnPropertyChanged();
                if (!_suppressCallback) _onEnabledChanged(this);
            }
        }
    }

    public string AssignedFaderBadge
    {
        get => _assignedFaderBadge;
        set => SetProperty(ref _assignedFaderBadge, value);
    }

    public bool IsAssignedToPhysicalFader
    {
        get => _isAssignedToPhysicalFader;
        set => SetProperty(ref _isAssignedToPhysicalFader, value);
    }

    public void SetFromHardwareOrProfile(byte rawVolume, bool muted, bool disabled)
    {
        _suppressCallback = true;
        VolumeRaw = rawVolume;
        IsMuted = muted;
        IsDisabled = disabled;
        _suppressCallback = false;
    }
}

public sealed record ChannelOptionItem(ChannelName Channel, string DisplayName);

public sealed class FaderSlotViewModel : ObservableObject
{
    private readonly Action<FaderSlotViewModel> _onAssignmentChanged;
    private readonly Action<FaderSlotViewModel> _onVolumeChanged;
    private readonly Action<FaderSlotViewModel> _onMuteToggled;
    private readonly Action<FaderSlotViewModel> _onEnabledToggled;
    private ChannelName _assignedChannel;
    private byte _volume = 255;
    private bool _isMuted;
    private bool _isChannelEnabled = true;
    private bool _suppressCallback;

    public FaderName Fader { get; }
    public string Title => $"Fader {Fader}";
    public string FaderNumberText => $"FADER {(int)Fader + 1}";
    public string HardwareButtonHint => $"HW Button {(int)Fader + 1}";
    public string IconGlyph => _assignedChannel switch
    {
        ChannelName.Mic => "🎙️",
        ChannelName.Chat => "💬",
        ChannelName.Music => "🎵",
        ChannelName.System => "💻",
        ChannelName.Game => "🎮",
        ChannelName.Console => "📻",
        ChannelName.LineIn => "🔌",
        ChannelName.Sample => "🔔",
        ChannelName.Headphones => "🎧",
        ChannelName.MicMonitor => "👂",
        ChannelName.LineOut => "🔊",
        _ => "🎚️"
    };

    public string FormattedDbLevel
    {
        get
        {
            if (_volume == 0) return "-∞ dB";
            double db = 20.0 * Math.Log10(_volume / 255.0);
            return $"{db:F1} dB";
        }
    }

    public IReadOnlyList<ChannelOptionItem> AssignableChannelOptions { get; } =
    [
        new(ChannelName.Mic, "🎙️ Mic Input"),
        new(ChannelName.Game, "🎮 Game Audio"),
        new(ChannelName.Music, "🎵 Music & Media"),
        new(ChannelName.Chat, "💬 Voice Chat"),
        new(ChannelName.System, "💻 System Audio"),
        new(ChannelName.Console, "📻 Console In"),
        new(ChannelName.LineIn, "🔌 Line-In (3.5mm)"),
        new(ChannelName.Sample, "🔔 Sampler"),
        new(ChannelName.Headphones, "🎧 Headphones Master"),
        new(ChannelName.LineOut, "🔊 Speakers Line-Out"),
        new(ChannelName.MicMonitor, "👂 Sidetone Monitor")
    ];

    public bool IsOutputChannel => _assignedChannel is ChannelName.Headphones or ChannelName.LineOut or ChannelName.MicMonitor;
    public string ChannelRoleBadgeText => IsOutputChannel ? "MASTER OUTPUT" : "INPUT SOURCE";
    public string ChannelRoleHintText => IsOutputChannel ? "Controls master output volume" : "Controls input source volume";
    public string ChannelRoleBackground => IsOutputChannel ? "#132B1E" : "#162436";
    public string ChannelRoleBorder => IsOutputChannel ? "#205E32" : "#25466B";
    public string ChannelRoleForeground => IsOutputChannel ? "#30D158" : "#47A1FF";

    public FaderSlotViewModel(
        FaderName fader,
        ChannelName initialChannel,
        Action<FaderSlotViewModel> onAssignmentChanged,
        Action<FaderSlotViewModel> onVolumeChanged,
        Action<FaderSlotViewModel> onMuteToggled,
        Action<FaderSlotViewModel> onEnabledToggled)
    {
        Fader = fader;
        _assignedChannel = initialChannel;
        _onAssignmentChanged = onAssignmentChanged;
        _onVolumeChanged = onVolumeChanged;
        _onMuteToggled = onMuteToggled;
        _onEnabledToggled = onEnabledToggled;
    }

    public ChannelName AssignedChannel
    {
        get => _assignedChannel;
        set
        {
            if (SetProperty(ref _assignedChannel, value))
            {
                OnPropertyChanged(nameof(ChannelDisplayName));
                OnPropertyChanged(nameof(IconGlyph));
                OnPropertyChanged(nameof(IsOutputChannel));
                OnPropertyChanged(nameof(ChannelRoleBadgeText));
                OnPropertyChanged(nameof(ChannelRoleHintText));
                OnPropertyChanged(nameof(ChannelRoleBackground));
                OnPropertyChanged(nameof(ChannelRoleBorder));
                OnPropertyChanged(nameof(ChannelRoleForeground));
                if (!_suppressCallback) _onAssignmentChanged(this);
            }
        }
    }

    public string ChannelDisplayName => DspTables.GetChannelDisplayName(_assignedChannel);

    public int VolumePercent
    {
        get => (int)Math.Round(_volume / 255.0 * 100.0);
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            byte raw = (byte)Math.Round(clamped / 100.0 * 255.0);
            if (_volume != raw)
            {
                _volume = raw;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedDbLevel));
                if (!_suppressCallback) _onVolumeChanged(this);
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                OnPropertyChanged(nameof(MuteButtonText));
                if (!_suppressCallback) _onMuteToggled(this);
            }
        }
    }

    public bool IsChannelEnabled
    {
        get => _isChannelEnabled;
        set
        {
            if (SetProperty(ref _isChannelEnabled, value))
            {
                OnPropertyChanged(nameof(IsChannelDisabled));
                if (!_suppressCallback) _onEnabledToggled(this);
            }
        }
    }

    public bool IsChannelDisabled => !_isChannelEnabled;
    public string DisabledHintText => $"Channel {ChannelDisplayName} is disabled. Click to enable.";

    public string MuteButtonText => _isMuted ? "Muted" : "Mute";

    public void SyncSilent(ChannelName channel, byte rawVolume, bool muted, bool enabled)
    {
        _suppressCallback = true;
        AssignedChannel = channel;
        _volume = rawVolume;
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(FormattedDbLevel));
        OnPropertyChanged(nameof(IconGlyph));
        OnPropertyChanged(nameof(IsOutputChannel));
        OnPropertyChanged(nameof(ChannelRoleBadgeText));
        OnPropertyChanged(nameof(ChannelRoleHintText));
        OnPropertyChanged(nameof(ChannelRoleBackground));
        OnPropertyChanged(nameof(ChannelRoleBorder));
        OnPropertyChanged(nameof(ChannelRoleForeground));
        IsMuted = muted;
        IsChannelEnabled = enabled;
        _suppressCallback = false;
    }
}

public sealed class RoutingRowViewModel : ObservableObject
{
    private readonly Action<BasicInputDevice, BasicOutputDevice, bool> _onRouteChanged;
    private readonly Action<BasicInputDevice, bool> _onInputDisabledChanged;
    private bool _headphones = true;
    private bool _broadcastMix = true;
    private bool _chatMic = true;
    private bool _sampler = true;
    private bool _lineOut = true;
    private bool _isInputEnabled = true;
    private int _volumePercent = 100;
    private bool _isMuted;
    private bool _suppress;

    public BasicInputDevice Input { get; }
    public string InputName => DspTables.GetInputDisplayName(Input);
    public bool CanRouteToChatMic => Input != BasicInputDevice.Chat;
    public bool CanRouteToSampler => Input != BasicInputDevice.Samples;

    public string InputIconGlyph => Input switch
    {
        BasicInputDevice.Microphone => "🎙️",
        BasicInputDevice.Game => "🎮",
        BasicInputDevice.Music => "🎵",
        BasicInputDevice.Chat => "💬",
        BasicInputDevice.System => "💻",
        BasicInputDevice.Console => "📻",
        BasicInputDevice.LineIn => "🔌",
        BasicInputDevice.Samples => "🔔",
        _ => "🎚️"
    };

    public string InputSubtitle => Input switch
    {
        BasicInputDevice.Microphone => "XLR / 3.5mm Voice Input",
        BasicInputDevice.Game => "Windows Game Audio",
        BasicInputDevice.Music => "Spotify, Browser & Media",
        BasicInputDevice.Chat => "Discord & Voice Comms",
        BasicInputDevice.System => "Default Desktop Audio",
        BasicInputDevice.Console => "Optical / Console Input",
        BasicInputDevice.LineIn => "3.5mm Stereo Aux Input",
        BasicInputDevice.Samples => "Sampler Audio",
        _ => ""
    };

    public string WireColorHex => Input switch
    {
        BasicInputDevice.Microphone => "#00E5FF",
        BasicInputDevice.Game => "#30D158",
        BasicInputDevice.Music => "#FF375F",
        BasicInputDevice.Chat => "#0A84FF",
        BasicInputDevice.System => "#FFD60A",
        BasicInputDevice.Console => "#BF5AF2",
        BasicInputDevice.LineIn => "#FF9F0A",
        BasicInputDevice.Samples => "#64D2FF",
        _ => "#47A1FF"
    };

    public int VolumePercent
    {
        get => _volumePercent;
        set => SetProperty(ref _volumePercent, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetProperty(ref _isMuted, value);
    }

    public string RouteSummaryText
    {
        get
        {
            var active = new List<string>(5);
            if (_headphones) active.Add("Headphones");
            if (_broadcastMix) active.Add("Stream Mix");
            if (_chatMic && CanRouteToChatMic) active.Add("Chat Mic");
            if (_sampler && CanRouteToSampler) active.Add("Sampler");
            if (_lineOut) active.Add("Line Out");
            return active.Count > 0
                ? $"Sending to: {string.Join(", ", active)}"
                : "Not routed to any destination";
        }
    }

    public RoutingRowViewModel(
        BasicInputDevice input,
        Action<BasicInputDevice, BasicOutputDevice, bool> onRouteChanged,
        Action<BasicInputDevice, bool> onInputDisabledChanged)
    {
        Input = input;
        _onRouteChanged = onRouteChanged;
        _onInputDisabledChanged = onInputDisabledChanged;
    }

    public bool IsInputEnabled
    {
        get => _isInputEnabled;
        set
        {
            if (SetProperty(ref _isInputEnabled, value) && !_suppress)
            {
                _onInputDisabledChanged(Input, !value);
            }
        }
    }

    public bool IsStreamSafe => Input == BasicInputDevice.Music && Headphones && !BroadcastMix;
    public bool IsPrivateChat => Input == BasicInputDevice.Chat && Headphones && !BroadcastMix;

    public bool Headphones
    {
        get => _headphones;
        set
        {
            if (SetProperty(ref _headphones, value))
            {
                OnPropertyChanged(nameof(RouteSummaryText));
                OnPropertyChanged(nameof(IsStreamSafe));
                OnPropertyChanged(nameof(IsPrivateChat));
                if (!_suppress) _onRouteChanged(Input, BasicOutputDevice.Headphones, value);
            }
        }
    }

    public bool BroadcastMix
    {
        get => _broadcastMix;
        set
        {
            if (SetProperty(ref _broadcastMix, value))
            {
                OnPropertyChanged(nameof(RouteSummaryText));
                OnPropertyChanged(nameof(IsStreamSafe));
                OnPropertyChanged(nameof(IsPrivateChat));
                if (!_suppress) _onRouteChanged(Input, BasicOutputDevice.BroadcastMix, value);
            }
        }
    }

    public bool ChatMic
    {
        get => _chatMic;
        set
        {
            if (!CanRouteToChatMic) return;
            if (SetProperty(ref _chatMic, value))
            {
                OnPropertyChanged(nameof(RouteSummaryText));
                if (!_suppress) _onRouteChanged(Input, BasicOutputDevice.ChatMic, value);
            }
        }
    }

    public bool Sampler
    {
        get => _sampler;
        set
        {
            if (!CanRouteToSampler) return;
            if (SetProperty(ref _sampler, value))
            {
                OnPropertyChanged(nameof(RouteSummaryText));
                if (!_suppress) _onRouteChanged(Input, BasicOutputDevice.Sampler, value);
            }
        }
    }

    public bool LineOut
    {
        get => _lineOut;
        set
        {
            if (SetProperty(ref _lineOut, value))
            {
                OnPropertyChanged(nameof(RouteSummaryText));
                if (!_suppress) _onRouteChanged(Input, BasicOutputDevice.LineOut, value);
            }
        }
    }

    public void LoadFromProfile(IReadOnlyDictionary<BasicOutputDevice, bool> map, bool disabled)
    {
        _suppress = true;
        IsInputEnabled = !disabled;
        Headphones = map.GetValueOrDefault(BasicOutputDevice.Headphones, true);
        BroadcastMix = map.GetValueOrDefault(BasicOutputDevice.BroadcastMix, true);
        ChatMic = CanRouteToChatMic && map.GetValueOrDefault(BasicOutputDevice.ChatMic, Input == BasicInputDevice.Microphone);
        Sampler = CanRouteToSampler && map.GetValueOrDefault(BasicOutputDevice.Sampler, true);
        LineOut = map.GetValueOrDefault(BasicOutputDevice.LineOut, true);
        OnPropertyChanged(nameof(RouteSummaryText));
        OnPropertyChanged(nameof(IsStreamSafe));
        OnPropertyChanged(nameof(IsPrivateChat));
        _suppress = false;
    }
}

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly GoXlrDeviceController _device = new();
    public GoXlrDeviceController Device => _device;
    private readonly Dispatcher _uiDispatcher;
    private readonly SemaphoreSlim _connectSemaphore = new(1, 1);
    private readonly CancellationTokenSource _pollCts = new();
    private readonly Task _pollTask;
    private readonly DispatcherTimer _autoSaveTimer;
    private AppSettings _settings;
    private XlrProfile _profile;
    private bool _suppressApply;
    private bool _pendingAutoSave;
    private uint _lastButtonsMask;
    private readonly byte[] _faderLastSeen = new byte[4];
    // True until the physical fader positions have been successfully read AND pushed to the hardware DSP after a (re)connect.
    private bool _pendingFaderSync;
    // Per fader: true while the hardware has not yet reported a real position (Mini reports 0 until first movement).
    private readonly bool[] _faderUnreported = new bool[4];
    private readonly bool[] _faderPaused = new bool[4];
    private readonly byte[] _faderPauseReferenceHwVol = new byte[4];
    private int _selectedFaderLightingIndex; // 0=A, 1=B, 2=C, 3=D, 4=All
    private string _customHexInput = "00FFFF";
    private DateTime _coughPressStartUtc;
    private bool _coughMutedOnPress;
    private bool _isBleepHeld;

    // Volatile snapshot fields read by background LED animation worker without touching _profile
    private volatile string _cachedLightingEffect = "Cyberpunk";
    private volatile string _cachedAccentHex = "00FFFF";
    private volatile int _cachedFaderMutedMask;

    private string _connectionStatusText = "Not connected";
    private bool _isConnected;
    private double _micLevelDb = -72.2;
    private double _obsSmoothDb = -60.0;
    // The word label follows the loudest level over half a second, so it doesn't flip on every syllable.
    private double _obsStatusDb = -60.0;
    private double _obsStatusPeakDb = -60.0;
    private long _obsStatusNextTick;
    private double _obsPeakHoldDb = -60.0;
    private double _lastNotifiedSmoothDb = double.NaN;
    private double _lastNotifiedPeakDb = double.NaN;
    private long _peakHoldExpiryTick;
    private string _selectedProfileName = "Default";
    private string _newProfileName = "";
    private string _statusBannerMessage = "Ready";
    private int _selectedNavIndex;

    public int SelectedNavIndex
    {
        get => _selectedNavIndex;
        set
        {
            if (SetProperty(ref _selectedNavIndex, value))
            {
                OnPropertyChanged(nameof(IsVoiceTab));
                OnPropertyChanged(nameof(IsHeadphonesTab));
                OnPropertyChanged(nameof(IsMixerTab));
                OnPropertyChanged(nameof(IsRoutingTab));
                OnPropertyChanged(nameof(IsSettingsTab));
                OnPropertyChanged(nameof(IsLightingTab));
                OnPropertyChanged(nameof(LightingTabVisibility));
                OnPropertyChanged(nameof(VoiceTabVisibility));
                OnPropertyChanged(nameof(HeadphonesTabVisibility));
                OnPropertyChanged(nameof(MixerTabVisibility));
                OnPropertyChanged(nameof(RoutingTabVisibility));
                OnPropertyChanged(nameof(SettingsTabVisibility));
                if (value == 3)
                {
                    RaiseRoutingGraphChanged();
                }
                else if (value == 4)
                {
                    RefreshLegacyGoXlrStatus();
                }
            }
        }
    }

    public bool IsVoiceTab
    {
        get => _selectedNavIndex == 0;
        set { if (value) SelectedNavIndex = 0; }
    }

    public bool IsHeadphonesTab
    {
        get => _selectedNavIndex == 1;
        set { if (value) SelectedNavIndex = 1; }
    }

    public bool IsMixerTab
    {
        get => _selectedNavIndex == 2;
        set { if (value) SelectedNavIndex = 2; }
    }

    public bool IsRoutingTab
    {
        get => _selectedNavIndex == 3;
        set { if (value) SelectedNavIndex = 3; }
    }

    public bool IsSettingsTab
    {
        get => _selectedNavIndex == 4;
        set { if (value) SelectedNavIndex = 4; }
    }

    public bool IsLightingTab
    {
        get => _selectedNavIndex == 5;
        set { if (value) SelectedNavIndex = 5; }
    }

    public Visibility LightingTabVisibility => _selectedNavIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VoiceTabVisibility => _selectedNavIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HeadphonesTabVisibility => _selectedNavIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MixerTabVisibility => _selectedNavIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RoutingTabVisibility => _selectedNavIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsTabVisibility => _selectedNavIndex == 4 ? Visibility.Visible : Visibility.Collapsed;

    // Voice Segmented Sub-Navigation (0 = Mic & Calibrated Gain, 1 = Voice Sound Shaper, 2 = Background Noise Suppression)
    private int _voiceSubTabIndex;
    public int VoiceSubTabIndex
    {
        get => _voiceSubTabIndex;
        set
        {
            if (SetProperty(ref _voiceSubTabIndex, value))
            {
                OnPropertyChanged(nameof(IsVoiceSubTabMic));
                OnPropertyChanged(nameof(IsVoiceSubTabShaper));
                OnPropertyChanged(nameof(IsVoiceSubTabNoise));
                OnPropertyChanged(nameof(VoiceSubTabMicVisibility));
                OnPropertyChanged(nameof(VoiceSubTabShaperVisibility));
                OnPropertyChanged(nameof(VoiceSubTabNoiseVisibility));
                OnPropertyChanged(nameof(IsVoiceSubTabHeadphones));
                OnPropertyChanged(nameof(VoiceSubTabHeadphonesVisibility));
            }
        }
    }

    public bool IsVoiceSubTabMic
    {
        get => _voiceSubTabIndex == 0;
        set { if (value) VoiceSubTabIndex = 0; }
    }

    public bool IsVoiceSubTabShaper
    {
        get => _voiceSubTabIndex == 1;
        set { if (value) VoiceSubTabIndex = 1; }
    }

    public bool IsVoiceSubTabNoise
    {
        get => _voiceSubTabIndex == 2;
        set { if (value) VoiceSubTabIndex = 2; }
    }

    // Retained aliases for backward compatibility with testing suites
    public bool IsVoiceSubTabHeadphones
    {
        get => IsHeadphonesTab;
        set { if (value) IsHeadphonesTab = true; }
    }

    public Visibility VoiceSubTabMicVisibility => _voiceSubTabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VoiceSubTabShaperVisibility => _voiceSubTabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VoiceSubTabNoiseVisibility => _voiceSubTabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VoiceSubTabHeadphonesVisibility => HeadphonesTabVisibility;

    public void SelectVoiceSubTab(int index) => VoiceSubTabIndex = index;

    public ObservableCollection<FaderSlotViewModel> FaderSlots { get; } = [];
    public ObservableCollection<ChannelMixerItemViewModel> AllChannels { get; } = [];
    public ObservableCollection<RoutingRowViewModel> RoutingRows { get; } = [];
    public ObservableCollection<RoutingRowViewModel> ActiveRoutingRows { get; } = [];
    public ObservableCollection<RoutingRowViewModel> InactiveRoutingRows { get; } = [];
    public ObservableCollection<string> ProfileNames { get; } = [];
    public ObservableCollection<ProfileChipViewModel> ProfileItems { get; } = [];
    public ObservableCollection<ProfileCardViewModel> ProfileCards { get; } = [];

    // Audio Output Endpoints for Sidetone Monitoring
    private readonly AudioMonitorService _audioMonitorService = new();
    private long _lastReconnectAttemptTick;
    public ObservableCollection<AudioEndpointItem> PlaybackDevices { get; } = [];
    private AudioEndpointItem? _selectedPlaybackDevice;

    public AudioEndpointItem? SelectedPlaybackDevice
    {
        get => _selectedPlaybackDevice;
        set
        {
            if (SetProperty(ref _selectedPlaybackDevice, value))
            {
                if (value != null)
                {
                    _profile.MonitorOutputDeviceId = value.Id;
                    AutoSaveIfEnabled();
                    _audioMonitorService.StopMonitoring();
                    PushRoutingForInput(BasicInputDevice.Microphone);
                }
            }
        }
    }

    public void EnsureHeadphoneMonitoring()
    {
        _audioMonitorService.StopMonitoring();
    }

    // Lighting Studio Segmented Tabs (0 = Custom Color, 1 = Gradients, 2 = Hardware Animations)
    private int _lightingStudioTabIndex;
    public int LightingStudioTabIndex
    {
        get => _lightingStudioTabIndex;
        set
        {
            if (SetProperty(ref _lightingStudioTabIndex, value))
            {
                OnPropertyChanged(nameof(IsLightingCustomTab));
                OnPropertyChanged(nameof(IsLightingGradientsTab));
                OnPropertyChanged(nameof(IsLightingAnimationsTab));
                OnPropertyChanged(nameof(LightingCustomTabVisibility));
                OnPropertyChanged(nameof(LightingGradientsTabVisibility));
                OnPropertyChanged(nameof(LightingAnimationsTabVisibility));
            }
        }
    }

    public bool IsLightingCustomTab
    {
        get => _lightingStudioTabIndex == 0;
        set { if (value) LightingStudioTabIndex = 0; }
    }

    public bool IsLightingGradientsTab
    {
        get => _lightingStudioTabIndex == 1;
        set { if (value) LightingStudioTabIndex = 1; }
    }

    public bool IsLightingAnimationsTab
    {
        get => _lightingStudioTabIndex == 2;
        set { if (value) LightingStudioTabIndex = 2; }
    }

    public Visibility LightingCustomTabVisibility => _lightingStudioTabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LightingGradientsTabVisibility => _lightingStudioTabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LightingAnimationsTabVisibility => _lightingStudioTabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

    // Custom Color Controls (Hue + RGB)
    private double _customHue = 180.0;
    private byte _customRed = 0;
    private byte _customGreen = 255;
    private byte _customBlue = 255;
    private bool _suppressColorUpdate;

    public double CustomHue
    {
        get => _customHue;
        set
        {
            if (SetProperty(ref _customHue, value) && !_suppressColorUpdate)
            {
                _suppressColorUpdate = true;
                var (r, g, b) = HueToRgb(value);
                _customRed = r;
                _customGreen = g;
                _customBlue = b;
                OnPropertyChanged(nameof(CustomRed));
                OnPropertyChanged(nameof(CustomGreen));
                OnPropertyChanged(nameof(CustomBlue));
                CustomHexInput = $"{r:X2}{g:X2}{b:X2}";
                ApplyColorToSelectedFader(r, g, b);
                _suppressColorUpdate = false;
            }
        }
    }

    public byte CustomRed
    {
        get => _customRed;
        set
        {
            if (SetProperty(ref _customRed, value) && !_suppressColorUpdate)
            {
                _suppressColorUpdate = true;
                CustomHexInput = $"{_customRed:X2}{_customGreen:X2}{_customBlue:X2}";
                ApplyColorToSelectedFader(_customRed, _customGreen, _customBlue);
                _suppressColorUpdate = false;
            }
        }
    }

    public byte CustomGreen
    {
        get => _customGreen;
        set
        {
            if (SetProperty(ref _customGreen, value) && !_suppressColorUpdate)
            {
                _suppressColorUpdate = true;
                CustomHexInput = $"{_customRed:X2}{_customGreen:X2}{_customBlue:X2}";
                ApplyColorToSelectedFader(_customRed, _customGreen, _customBlue);
                _suppressColorUpdate = false;
            }
        }
    }

    public byte CustomBlue
    {
        get => _customBlue;
        set
        {
            if (SetProperty(ref _customBlue, value) && !_suppressColorUpdate)
            {
                _suppressColorUpdate = true;
                CustomHexInput = $"{_customRed:X2}{_customGreen:X2}{_customBlue:X2}";
                ApplyColorToSelectedFader(_customRed, _customGreen, _customBlue);
                _suppressColorUpdate = false;
            }
        }
    }

    // Quick Start Audio Setup Wizard
    private bool _isQuickStartWizardOpen;
    private int _quickStartStep = 1;

    public bool IsQuickStartWizardOpen
    {
        get => _isQuickStartWizardOpen;
        set => SetProperty(ref _isQuickStartWizardOpen, value);
    }

    public int QuickStartStep
    {
        get => _quickStartStep;
        set
        {
            if (SetProperty(ref _quickStartStep, value))
            {
                OnPropertyChanged(nameof(IsQuickStartStep1));
                OnPropertyChanged(nameof(IsQuickStartStep2));
                OnPropertyChanged(nameof(IsQuickStartStep3));
                OnPropertyChanged(nameof(IsQuickStartStep4));
                OnPropertyChanged(nameof(QuickStartStepTitle));
                OnPropertyChanged(nameof(QuickStartStepDescription));
            }
        }
    }

    public bool IsQuickStartStep1 => _quickStartStep == 1;
    public bool IsQuickStartStep2 => _quickStartStep == 2;
    public bool IsQuickStartStep3 => _quickStartStep == 3;
    public bool IsQuickStartStep4 => _quickStartStep == 4;

    public string QuickStartStepTitle => _quickStartStep switch
    {
        1 => "Step 1: Microphone & Gain Calibration",
        2 => "Step 2: Headphone Sidetone & Monitoring Output",
        3 => "Step 3: Discord Pro & Krisp Bypass Setup",
        4 => "Step 4: Stream Safe Routing (DMCA Shield)",
        _ => "Quick Start Audio Setup"
    };

    public string QuickStartStepDescription => _quickStartStep switch
    {
        1 => "Choose your microphone type and calibrate your preamp gain so spoken speech comfortably hits the green and yellow sweet spot on the VU meter.",
        2 => "Select where you want to hear your voice. Choose GoXLR 3.5mm direct or any PC headphone device (USB DAC, AirPods, headset) for low-latency WASAPI sidetone.",
        3 => "Turn off Krisp in Discord! Project XLR's onboard hardware DSP gate and compressor isolate your voice cleanly with 0% CPU usage and zero robotic voice artifacts.",
        4 => "Isolate copyrighted Spotify or Apple Music from your Broadcast Stream Mix with one click so Twitch and YouTube VODs never receive copyright strikes.",
        _ => ""
    };

    public void OpenQuickStartWizard()
    {
        QuickStartStep = 1;
        IsQuickStartWizardOpen = true;
    }

    public void CloseQuickStartWizard()
    {
        IsQuickStartWizardOpen = false;
    }

    public void QuickStartNext()
    {
        if (QuickStartStep < 4) QuickStartStep++;
        else CloseQuickStartWizard();
    }

    public void QuickStartPrev()
    {
        if (QuickStartStep > 1) QuickStartStep--;
    }
    private double _animPhase;

    public ChannelMixerItemViewModel? HeadphonesMasterChannel => AllChannels.FirstOrDefault(c => c.Channel == ChannelName.Headphones);
    public ChannelMixerItemViewModel? LineOutMasterChannel => AllChannels.FirstOrDefault(c => c.Channel == ChannelName.LineOut);

    public Visibility HasInactiveRoutingRows => InactiveRoutingRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<MicrophoneType> MicTypes { get; } = Enum.GetValues<MicrophoneType>();
    public IReadOnlyList<string> CompressorRatioOptions => DspTables.CompressorRatioLabels;
    public IReadOnlyList<string> CompressorAttackOptions => DspTables.CompressorAttackLabels;
    public IReadOnlyList<string> CompressorReleaseOptions => DspTables.CompressorReleaseLabels;
    public IReadOnlyList<int> GateTimeOptions => DspTables.GateTimesMs;

    /// <summary>Set by --screenshots: renders the UI without touching the GoXLR or saving anything.</summary>
    public static bool PreviewMode { get; set; }

    public MainViewModel(bool connectSynchronously = false)
    {
        _uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _settings = ProfileManager.LoadSettings();
        if (ProfileManager.ListProfileNames().Count == 0)
        {
            int count = ProfileManager.ImportOfficialGoXlrProfiles();
            if (count > 0)
            {
                _statusBannerMessage = $"Imported {count} existing GoXLR profile(s) automatically.";
            }
        }

        _profile = ProfileManager.LoadProfile(_settings.ActiveProfileName);
        _selectedProfileName = _profile.Name;
        RefreshProfileList();

        foreach (var fader in Enum.GetValues<FaderName>())
        {
            FaderSlots.Add(new FaderSlotViewModel(
                fader,
                _profile.FaderAssignments.GetValueOrDefault(fader, ChannelName.Mic),
                OnFaderAssignmentChanged,
                OnFaderVolumeChanged,
                OnFaderMuteChanged,
                OnFaderEnabledChanged));
        }

        foreach (var ch in Enum.GetValues<ChannelName>())
        {
            AllChannels.Add(new ChannelMixerItemViewModel(
                ch,
                OnChannelVolumeChanged,
                OnChannelMuteChanged,
                OnChannelEnabledChanged));
        }

        foreach (var inp in Enum.GetValues<BasicInputDevice>())
        {
            RoutingRows.Add(new RoutingRowViewModel(
                inp,
                OnRouteChanged,
                OnInputDisabledChanged));
        }

        _device.LogMessage += msg => RunOnUi(() => StatusBannerMessage = msg);

        _autoSaveTimer = new DispatcherTimer(DispatcherPriority.Background, _uiDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            if (_pendingAutoSave && _settings.AutoSaveProfiles)
            {
                _pendingAutoSave = false;
                ProfileManager.SaveProfile(_profile);
            }
        };

        _deepFilterNetService.StatusChanged += () => RunOnUi(RefreshDeepFilterNetProperties);
        _deepFilterNetDenoiseStrength = _deepFilterNetService.ReadDenoiseStrength();
        RefreshLegacyGoXlrStatus();

        SyncViewModelsFromProfile();
        UpdateAnimationCache();

        PlaybackDevices.Clear();
        foreach (var dev in AudioMonitorService.GetAvailablePlaybackDevices())
        {
            PlaybackDevices.Add(dev);
        }

        _selectedPlaybackDevice = PlaybackDevices.FirstOrDefault();

        if (PreviewMode)
        {
            _connectionStatusText = "Preview";
            _pollTask = Task.CompletedTask;
        }
        else if (connectSynchronously)
        {
            TryConnectDevice();
            _pollTask = Task.CompletedTask;
        }
        else
        {
            _connectionStatusText = "Connecting to GoXLR Mini...";
            _pollTask = Task.Run(() => RunHardwarePollLoopAsync(_pollCts.Token));
        }

        UpdateMicMonitor();
    }

    private readonly MicMonitorEngine _micMonitor = new();

    /// <summary>Runs the headphone monitor whenever Hear myself or the mic test needs it.</summary>
    private void UpdateMicMonitor()
    {
        if (PreviewMode) return;

        if (!SidetoneEnabled && !IsMicCheckActive)
        {
            _micMonitor.Stop();
            return;
        }

        _micMonitor.Volume = IsMicCheckActive ? 1f : SidetoneVolumePercent / 100f;
        if (_micMonitor.Start() is { } error)
        {
            StatusBannerMessage = error;
        }
    }

    private void RunOnUi(Action action)
    {
        if (_isDisposed) return;
        if (_uiDispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = _uiDispatcher.InvokeAsync(() =>
            {
                if (!_isDisposed) action();
            }, DispatcherPriority.DataBind);
        }
    }

    private void UpdateAnimationCache()
    {
        _cachedLightingEffect = _profile.LightingEffect ?? "Cyberpunk";
        _cachedAccentHex = _profile.AccentHexColor ?? "00FFFF";
        int mask = 0;
        ReadOnlySpan<FaderName> faders = [FaderName.A, FaderName.B, FaderName.C, FaderName.D];
        for (int i = 0; i < 4; i++)
        {
            var ch = _profile.FaderAssignments.GetValueOrDefault(faders[i], ChannelName.Mic);
            if (_profile.ChannelMuted.GetValueOrDefault(ch, false) || IsChannelCurrentlyDisabled(ch))
            {
                mask |= (1 << i);
            }
        }
        _cachedFaderMutedMask = mask;
    }

    public event EventHandler? RoutingGraphChanged;
    private void RaiseRoutingGraphChanged() => RoutingGraphChanged?.Invoke(this, EventArgs.Empty);

    private bool _isVoiceFineTuneExpanded;
    public bool IsVoiceFineTuneExpanded
    {
        get => _isVoiceFineTuneExpanded;
        set
        {
            if (SetProperty(ref _isVoiceFineTuneExpanded, value))
            {
                OnPropertyChanged(nameof(VoiceFineTuneVisibility));
            }
        }
    }
    public Visibility VoiceFineTuneVisibility => _isVoiceFineTuneExpanded ? Visibility.Visible : Visibility.Collapsed;

    private bool _isCompactWindowMode;
    public bool IsCompactWindowMode
    {
        get => _isCompactWindowMode;
        set
        {
            if (SetProperty(ref _isCompactWindowMode, value))
            {
                OnPropertyChanged(nameof(ExpandedWindowDetailVisibility));
            }
        }
    }
    public Visibility ExpandedWindowDetailVisibility => _isCompactWindowMode ? Visibility.Collapsed : Visibility.Visible;

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set => SetProperty(ref _connectionStatusText, value);
    }

    public string StatusBannerMessage
    {
        get => _statusBannerMessage;
        set => SetProperty(ref _statusBannerMessage, value);
    }

    public double MicLevelDb
    {
        get => _micLevelDb;
        private set
        {
            if (SetProperty(ref _micLevelDb, value))
            {
                OnPropertyChanged(nameof(MicLevelPercent));
                OnPropertyChanged(nameof(MicLevelText));
                OnPropertyChanged(nameof(IsGateOpen));
                OnPropertyChanged(nameof(GateStatusText));
            }
        }
    }

    // Map -72.2..0 dB to 0..100%
    public double MicLevelPercent => Math.Clamp((_micLevelDb + 72.2) / 72.2 * 100.0, 0.0, 100.0);
    public string MicLevelText => $"{_micLevelDb:F1} dB";
    public bool IsGateOpen => !GateEnabled || _micLevelDb >= GateThresholdDb;
    public string GateStatusText => !GateEnabled ? "Gate Bypassed" : (IsGateOpen ? "Gate Open" : "Gate Closed");

    // Global Mic Mute & Bleep state (synced with hardware Cough & Bleep buttons)
    public bool IsMicMuted
    {
        get => _profile.ChannelMuted.GetValueOrDefault(ChannelName.Mic, false);
        set
        {
            if (IsMicMuted != value)
            {
                SetChannelMuteState(ChannelName.Mic, value, value ? "Microphone Muted" : "Microphone Live");
            }
        }
    }

    public string MicMuteStatusText => IsMicMuted ? "Mic Muted" : "Mic Live";

    // --- OBS STUDIO-GRADE VU PEAK METER WITH BALLISTICS & PEAK HOLD ---
    public double ObsLevelDb => _obsSmoothDb;
    public double ObsPeakHoldDb => _obsPeakHoldDb;
    public double ObsLevelPercent => DbToObsMeterPercent(_obsSmoothDb);
    public double ObsPeakHoldPercent => DbToObsMeterPercent(_obsPeakHoldDb);
    public bool IsClipping => _obsSmoothDb >= -1.0;
    public string ObsPeakHoldText => _obsPeakHoldDb <= -59.5 ? "-∞ dB" : $"{_obsPeakHoldDb:F1} dB";
    public string ObsLevelText => _obsSmoothDb <= -59.5 ? "-∞ dB" : $"{_obsSmoothDb:F1} dB";
    public string ObsLevelStatusText => _obsStatusDb switch
    {
        >= -1.0 => "Too loud: lower Mic level",
        > -6.0 and < -1.0 => "Loud",
        >= -30.0 and <= -6.0 => "Ideal",
        > -48.0 and < -30.0 => "Quiet",
        _ => "Silent"
    };

    private static readonly SolidColorBrush MeterClipBrush = CreateFrozenBrush(255, 69, 58);      // Red (#FF453A) - Clip / Peak
    private static readonly SolidColorBrush MeterLoudBrush = CreateFrozenBrush(255, 214, 10);     // Yellow / Amber (#FFD60A) - Loud vocal
    private static readonly SolidColorBrush MeterSweetSpotBrush = CreateFrozenBrush(48, 209, 88); // Vibrant Green (#30D158) - Ideal speaking zone
    private static readonly SolidColorBrush MeterLowBrush = CreateFrozenBrush(30, 160, 60);       // Muted Green - Low room ambience
    private static readonly SolidColorBrush MeterIdleStatusBrush = CreateFrozenBrush(140, 140, 150);

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public Brush ObsMeterBrush => _obsSmoothDb switch
    {
        >= -3.0 => MeterClipBrush,
        >= -12.0 => MeterLoudBrush,
        >= -32.0 => MeterSweetSpotBrush,
        _ => MeterLowBrush
    };

    public Brush ObsMeterStatusBrush => _obsStatusDb switch
    {
        >= -3.0 => MeterClipBrush,
        >= -12.0 => MeterLoudBrush,
        >= -32.0 => MeterSweetSpotBrush,
        _ => MeterIdleStatusBrush
    };

    public static double DbToObsMeterPercent(double db)
    {
        if (db <= -60.0) return 0.0;
        if (db >= 0.0) return 100.0;
        if (db < -24.0)
        {
            return (db + 60.0) / 36.0 * 58.0;
        }
        if (db < -14.0)
        {
            return 58.0 + ((db + 24.0) / 10.0 * 20.0);
        }
        if (db < -6.0)
        {
            return 78.0 + ((db + 14.0) / 8.0 * 10.0);
        }
        return 88.0 + ((db + 6.0) / 6.0 * 12.0);
    }

    public GridLength ObsMeterActiveWidth => new(Math.Max(0.001, ObsLevelPercent), GridUnitType.Star);
    public GridLength ObsMeterRemainingWidth => new(Math.Max(0.001, 100.0 - ObsLevelPercent), GridUnitType.Star);
    public GridLength ObsPeakHoldActiveWidth => new(Math.Max(0.001, ObsPeakHoldPercent), GridUnitType.Star);
    public GridLength ObsPeakHoldRemainingWidth => new(Math.Max(0.001, 100.0 - ObsPeakHoldPercent), GridUnitType.Star);

    // --- HARDWARE BACKGROUND NOISE SUPPRESSION (3 SELECTABLE MODES) ---
    public bool StudioNoiseSuppressionEnabled
    {
        get => _profile.StudioNoiseSuppressionEnabled;
        set
        {
            if (_profile.StudioNoiseSuppressionEnabled != value)
            {
                _profile.StudioNoiseSuppressionEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StudioNoiseSuppressionStatusText));
                OnPropertyChanged(nameof(IsAiDoubleSuppressionConflict));
                ApplyStudioNoiseSuppression();
                AutoSaveIfEnabled();
            }
        }
    }

    public string StudioNoiseSuppressionStatusText => StudioNoiseSuppressionEnabled
        ? $"Active: {StudioNoiseSuppressionPresetLabel}"
        : "Off (Unfiltered Microphone)";

    public string StudioNoiseSuppressionPreset
    {
        get => _profile.StudioNoiseSuppressionPreset;
        set
        {
            if (_profile.StudioNoiseSuppressionPreset != value)
            {
                _profile.StudioNoiseSuppressionPreset = value;
                _profile.StudioNoiseSuppressionEnabled = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StudioNoiseSuppressionEnabled));
                OnPropertyChanged(nameof(StudioNoiseSuppressionPresetLabel));
                OnPropertyChanged(nameof(StudioNoiseSuppressionStatusText));
                OnPropertyChanged(nameof(IsPresetStudioClean));
                OnPropertyChanged(nameof(IsPresetKeyboardRejection));
                OnPropertyChanged(nameof(IsPresetRawStudio));
                OnPropertyChanged(nameof(IsAiDoubleSuppressionConflict));
                ApplyStudioNoiseSuppression();
                AutoSaveIfEnabled();
            }
        }
    }

    public string StudioNoiseSuppressionPresetLabel => _profile.StudioNoiseSuppressionPreset switch
    {
        "KeyboardRejection" => "Keyboard & Click Blocker (Aggressive)",
        "RawStudio" => "Natural Room (Light)",
        _ => "Studio Clean (Balanced)"
    };

    public bool IsPresetStudioClean
    {
        get => _profile.StudioNoiseSuppressionPreset == "StudioClean";
        set { if (value) StudioNoiseSuppressionPreset = "StudioClean"; }
    }

    public bool IsPresetKeyboardRejection
    {
        get => _profile.StudioNoiseSuppressionPreset == "KeyboardRejection";
        set { if (value) StudioNoiseSuppressionPreset = "KeyboardRejection"; }
    }

    public bool IsPresetRawStudio
    {
        get => _profile.StudioNoiseSuppressionPreset == "RawStudio";
        set { if (value) StudioNoiseSuppressionPreset = "RawStudio"; }
    }

    public void ApplyStudioNoiseSuppression()
    {
        if (!_profile.StudioNoiseSuppressionEnabled)
        {
            _profile.GateEnabled = false;
            _profile.CompressorEnabled = false;
            _profile.DeEsserEnabled = false;
            ApplyNoiseGateToHardware();
            ApplyCompressorToHardware();
            _device.SetEffectParams([(EffectKey.DeEsser, 0)]);
            SyncViewModelsFromProfile();
            StatusBannerMessage = "Background Noise Suppression turned off (Unfiltered Mic).";
            return;
        }

        _profile.GateEnabled = true;
        _profile.CompressorEnabled = true;
        _profile.DeEsserEnabled = true;

        switch (_profile.StudioNoiseSuppressionPreset)
        {
            case "KeyboardRejection":
                _profile.GateThresholdDb = -34;
                _profile.GateAttenuationPercent = 100;
                _profile.GateAttackIndex = 0;
                _profile.GateReleaseIndex = 9; // 100ms fast release
                _profile.CompressorThresholdDb = -20;
                _profile.CompressorRatioIndex = 9; // 4.0:1
                _profile.CompressorMakeUpGainDb = 5;
                _profile.DeEsserPercent = 20;
                StatusBannerMessage = "Keyboard & Click Blocker active (-34 dB fast cutoff + 4:1 leveler).";
                break;

            case "RawStudio":
                _profile.GateThresholdDb = -52;
                _profile.GateAttenuationPercent = 70;
                _profile.GateAttackIndex = 0;
                _profile.GateReleaseIndex = 25;
                _profile.CompressorThresholdDb = -14;
                _profile.CompressorRatioIndex = 5; // 1.8:1
                _profile.CompressorMakeUpGainDb = 2;
                _profile.DeEsserPercent = 5;
                StatusBannerMessage = "Natural Room (Light) active (-52 dB gentle gate + 1.8:1 leveler).";
                break;

            default:
                _profile.GateThresholdDb = -42;
                _profile.GateAttenuationPercent = 100;
                _profile.GateAttackIndex = 0;
                _profile.GateReleaseIndex = 19; // 200ms natural speech decay
                _profile.CompressorThresholdDb = -18;
                _profile.CompressorRatioIndex = 8; // 3.2:1 optical curve
                _profile.CompressorMakeUpGainDb = 4;
                _profile.DeEsserPercent = 15;
                StatusBannerMessage = "Studio Clean (Balanced) active (-42 dB gate + 3.2:1 leveler).";
                break;
        }

        ApplyNoiseGateToHardware();
        ApplyCompressorToHardware();
        _device.SetEffectParams([(EffectKey.DeEsser, _profile.DeEsserPercent)]);
        SyncViewModelsFromProfile();
    }

    // --- NEURAL AI NOISE SUPPRESSION (DEEPFILTERNET 3) ---
    private readonly DeepFilterNetService _deepFilterNetService = new();

    public DeepFilterNetStatus DeepFilterNetStatus => _deepFilterNetService.CurrentStatus;
    public string DeepFilterNetTitle => _deepFilterNetService.StatusTitle;
    public string DeepFilterNetMessage => _deepFilterNetService.StatusMessage;
    public string DeepFilterNetBadgeColor => _deepFilterNetService.StatusBadgeColor;
    public bool IsDeepFilterNetActive => _deepFilterNetService.IsActive;
    public bool IsDeepFilterNetInstalled => _deepFilterNetService.IsInstalled;
    public bool IsDeepFilterNetHooked => _deepFilterNetService.IsChatMicHooked;
    public bool IsDeepFilterNetPluginReady => _deepFilterNetService.IsPluginInstalled;
    public string DeepFilterNetStatusTextShort => DeepFilterNetStatus switch
    {
        DeepFilterNetStatus.Active => "On",
        DeepFilterNetStatus.Paused => "Paused",
        DeepFilterNetStatus.NeedsHook => "Needs setup",
        DeepFilterNetStatus.PluginMissing => "Not installed",
        _ => "Off"
    };

    public string MicTypeDisplay => _profile.MicType switch
    {
        MicrophoneType.Dynamic => "Dynamic (XLR)",
        MicrophoneType.Condenser => "Condenser (+48V)",
        MicrophoneType.Jack => "3.5mm Headset",
        _ => "Dynamic (XLR)"
    };

    public string DeepFilterNetEqualizerApoStatusText => IsDeepFilterNetInstalled ? "✅ Installed" : "❌ Not Installed";
    public string DeepFilterNetEqualizerApoStatusColor => IsDeepFilterNetInstalled ? "#30D158" : "#8E8E98";

    public string DeepFilterNetChatMicHookStatusText => IsDeepFilterNetHooked ? "✅ Active on Chat Mic" : "⚠️ Chat Mic Not Hooked";
    public string DeepFilterNetChatMicHookStatusColor => IsDeepFilterNetHooked ? "#30D158" : "#FF9F0A";

    public string DeepFilterNetPluginStatusText => IsDeepFilterNetPluginReady ? "✅ Ready (Alt-Denoiser.dll)" : "❌ Plugin Missing";
    public string DeepFilterNetPluginStatusColor => IsDeepFilterNetPluginReady ? "#30D158" : "#FF453A";

    public bool DeepFilterNetToggleEnabled
    {
        get => _deepFilterNetService.IsEnabledInConfig;
        set
        {
            _deepFilterNetService.ToggleEnabled(value);
            OnPropertyChanged();
            RefreshDeepFilterNetProperties();
        }
    }

    public string DeepFilterNetActionButtonText => _deepFilterNetService.CurrentStatus switch
    {
        DeepFilterNetStatus.NotInstalled => "⚡ 1-Click Install DeepFilterNet 3",
        DeepFilterNetStatus.NeedsHook => "🔧 Hook Chat Mic in Configurator",
        DeepFilterNetStatus.PluginMissing => "🔧 Repair Plugin Files",
        DeepFilterNetStatus.Paused => "▶️ Resume DeepFilterNet 3 AI",
        DeepFilterNetStatus.Active => "⚙️ Configure / Repair DeepFilterNet 3",
        _ => "⚡ Setup DeepFilterNet 3 AI"
    };

    public async Task InstallOrRepairDeepFilterNetAsync()
    {
        StatusBannerMessage = "Setting up DeepFilterNet 3 AI Noise Suppression for GoXLR Chat Mic...";
        bool ok = await _deepFilterNetService.InstallOrRepairAsync();
        RefreshDeepFilterNetProperties();
        StatusBannerMessage = ok
            ? "DeepFilterNet 3 setup completed! Verify 'Chat Mic' is selected in Configurator."
            : "DeepFilterNet 3 setup canceled or encountered an error.";
    }

    public void OpenDeepFilterNetConfigurator()
    {
        _deepFilterNetService.OpenConfigurator();
    }

    public void OpenDeepFilterNetFolder()
    {
        _deepFilterNetService.OpenConfigFolder();
    }

    public void RefreshDeepFilterNetStatus()
    {
        _deepFilterNetService.RefreshStatus();
        RefreshDeepFilterNetProperties();
    }

    private void RefreshDeepFilterNetProperties()
    {
        OnPropertyChanged(nameof(DeepFilterNetStatus));
        OnPropertyChanged(nameof(DeepFilterNetTitle));
        OnPropertyChanged(nameof(DeepFilterNetMessage));
        OnPropertyChanged(nameof(DeepFilterNetBadgeColor));
        OnPropertyChanged(nameof(IsDeepFilterNetActive));
        OnPropertyChanged(nameof(IsDeepFilterNetInstalled));
        OnPropertyChanged(nameof(IsDeepFilterNetHooked));
        OnPropertyChanged(nameof(IsDeepFilterNetPluginReady));
        OnPropertyChanged(nameof(DeepFilterNetEqualizerApoStatusText));
        OnPropertyChanged(nameof(DeepFilterNetEqualizerApoStatusColor));
        OnPropertyChanged(nameof(DeepFilterNetChatMicHookStatusText));
        OnPropertyChanged(nameof(DeepFilterNetChatMicHookStatusColor));
        OnPropertyChanged(nameof(DeepFilterNetPluginStatusText));
        OnPropertyChanged(nameof(DeepFilterNetPluginStatusColor));
        OnPropertyChanged(nameof(DeepFilterNetToggleEnabled));
        OnPropertyChanged(nameof(DeepFilterNetActionButtonText));
        OnPropertyChanged(nameof(IsAiDoubleSuppressionConflict));
    }

    // --- SMART AUDIO ADVISOR & CONFLICT DETECTOR ---
    public bool IsAiDoubleSuppressionConflict =>
        IsDeepFilterNetActive &&
        StudioNoiseSuppressionEnabled &&
        (IsPresetKeyboardRejection || _profile.GateThresholdDb >= -38) &&
        _profile.GateThresholdDb != _profile.VoiceCheckGateDb; // A measured gate already leaves room for the voice

    public void AutoHarmonizeSuppression()
    {
        StudioNoiseSuppressionPreset = "RawStudio"; // Sets gate to -52dB gentle cutoff
        StatusBannerMessage = "✨ Audio Harmonized: Hardware gate relaxed to Natural Room (-52 dB). DeepFilterNet 3 AI removes keyboard clicks with zero clipped word endings!";
        OnPropertyChanged(nameof(IsAiDoubleSuppressionConflict));
    }

    private string _deepFilterNetDenoiseStrength = "Balanced";
    public string DeepFilterNetDenoiseStrength
    {
        get => _deepFilterNetDenoiseStrength;
        set
        {
            if (SetProperty(ref _deepFilterNetDenoiseStrength, value))
            {
                _deepFilterNetService.SetDenoiseStrength(value);
                OnPropertyChanged(nameof(IsDenoiseGentle));
                OnPropertyChanged(nameof(IsDenoiseBalanced));
                OnPropertyChanged(nameof(IsDenoiseAggressive));
                StatusBannerMessage = $"DeepFilterNet 3 AI strength set to {value}.";
            }
        }
    }

    public bool IsDenoiseGentle
    {
        get => _deepFilterNetDenoiseStrength == "Gentle";
        set { if (value) DeepFilterNetDenoiseStrength = "Gentle"; }
    }

    public bool IsDenoiseBalanced
    {
        get => _deepFilterNetDenoiseStrength == "Balanced";
        set { if (value) DeepFilterNetDenoiseStrength = "Balanced"; }
    }

    public bool IsDenoiseAggressive
    {
        get => _deepFilterNetDenoiseStrength == "Aggressive";
        set { if (value) DeepFilterNetDenoiseStrength = "Aggressive"; }
    }

    public void LaunchAltDenoiserStandalone()
    {
        _deepFilterNetService.LaunchStandaloneVisualizer();
        StatusBannerMessage = "Launched DeepFilterNet 3 standalone interactive visualizer.";
    }

    public bool IsMicCheckActive
    {
        get => _audioMonitorService.IsMicCheckActive;
        set
        {
            if (_audioMonitorService.IsMicCheckActive != value)
            {
                _audioMonitorService.SetMicCheckActive(value);
                UpdateMicMonitor();
                OnPropertyChanged(nameof(IsMicCheckActive));
                OnPropertyChanged(nameof(MicCheckButtonText));
                OnPropertyChanged(nameof(MicCheckBadgeColor));

                if (value)
                {
                    StatusBannerMessage = "🎧 Mic Check ACTIVE: You are listening directly to the DeepFilterNet 3 denoised Chat Mic in your headphones. Type or talk to hear the AI silence background noise!";
                }
                else
                {
                    StatusBannerMessage = "🎧 Mic Check stopped: Returned to standard desktop & game audio monitoring.";
                }

                PushRoutingForInput(BasicInputDevice.Microphone);
            }
        }
    }

    public string MicCheckButtonText => IsMicCheckActive
        ? "Stop test"
        : "Start test";

    public string MicCheckBadgeColor => IsMicCheckActive ? "#30D158" : "#47A1FF";

    public void ToggleMicCheck() => IsMicCheckActive = !IsMicCheckActive;

    // --- PROFILE MACRO TUNING SLIDERS ---
    public int ProfileVocalTonePercent
    {
        get => _profile.ProfileVocalTonePercent;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (_profile.ProfileVocalTonePercent != clamped)
            {
                _profile.ProfileVocalTonePercent = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProfileVocalToneLabel));
                ApplyMacroVocalTone(clamped);
                AutoSaveIfEnabled();
            }
        }
    }

    public string ProfileVocalToneLabel => _profile.ProfileVocalTonePercent switch
    {
        < 30 => "Crisp Clarity (High Presence)",
        > 70 => "Deep Radio Warmth (Broadcast Proximity)",
        _ => "Balanced Studio Voice"
    };

    private void ApplyMacroVocalTone(int percent)
    {
        float t = percent / 100.0f;
        int eq90 = (int)Math.Round(-3.0 + (t * 9.0)); // -3 to +6 dB
        int eq250 = (int)Math.Round(-2.0 + (t * 6.0)); // -2 to +4 dB
        int eq3k = (int)Math.Round(3.0 - (t * 3.0));   // +3 to 0 dB
        int eq8k = (int)Math.Round(4.0 - (t * 4.0));   // +4 to 0 dB

        _profile.Eq90HzGain = eq90;
        _profile.Eq250HzGain = eq250;
        _profile.Eq3KHzGain = eq3k;
        _profile.Eq8KHzGain = eq8k;

        OnPropertyChanged(nameof(Eq90HzGain));
        OnPropertyChanged(nameof(Eq250HzGain));
        OnPropertyChanged(nameof(Eq3KHzGain));
        OnPropertyChanged(nameof(Eq8KHzGain));

        ApplyEqToHardware();
    }

    public int ProfileNoiseRejectionPercent
    {
        get => _profile.ProfileNoiseRejectionPercent;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (_profile.ProfileNoiseRejectionPercent != clamped)
            {
                _profile.ProfileNoiseRejectionPercent = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProfileNoiseRejectionLabel));
                ApplyMacroNoiseRejection(clamped);
                AutoSaveIfEnabled();
            }
        }
    }

    public string ProfileNoiseRejectionLabel => _profile.ProfileNoiseRejectionPercent switch
    {
        < 30 => "Gentle (Quiet Room / Transparent)",
        > 70 => "Aggressive (Mechanical Keyboard Rejection)",
        _ => "Studio Clean (Recommended for Discord)"
    };

    private void ApplyMacroNoiseRejection(int percent)
    {
        float t = percent / 100.0f;
        int thresh = (int)Math.Round(-56.0 + (t * 28.0)); // -56 to -28 dB
        int atten = percent < 30 ? 70 : 100;
        int releaseIdx = (int)Math.Round(22.0 - (t * 13.0)); // 22 to 9

        _profile.GateThresholdDb = thresh;
        _profile.GateAttenuationPercent = atten;
        _profile.GateReleaseIndex = releaseIdx;

        OnPropertyChanged(nameof(GateThresholdDb));
        OnPropertyChanged(nameof(GateAttenuationPercent));
        OnPropertyChanged(nameof(GateReleaseIndex));

        ApplyNoiseGateToHardware();
    }

    // --- CENSOR BLEEP (#!*@) ---
    public bool IsBleepHeld
    {
        get => _isBleepHeld;
        set
        {
            if (SetProperty(ref _isBleepHeld, value))
            {
                OnPropertyChanged(nameof(BleepActiveVisibility));
                UpdateButtonLeds();
                StatusBannerMessage = value ? "Censor Bleep (#!*@) Active" : "Censor Bleep Stopped";
            }
        }
    }

    public Visibility BleepActiveVisibility => _isBleepHeld ? Visibility.Visible : Visibility.Collapsed;

    public int BleepLevelDb
    {
        get => _profile.BleepLevelDb;
        set
        {
            int clamped = Math.Clamp(value, -36, 0);
            if (_profile.BleepLevelDb != clamped)
            {
                _profile.BleepLevelDb = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BleepVolumePercent));
                _device.SetBleepLevel(clamped);
                AutoSaveIfEnabled();
            }
        }
    }

    public int BleepVolumePercent
    {
        get => (int)Math.Round((_profile.BleepLevelDb + 36.0) / 36.0 * 100.0);
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            int db = (int)Math.Round(-36.0 + (clamped / 100.0 * 36.0));
            BleepLevelDb = db;
        }
    }

    // Startup Level Behavior setting
    public StartupLevelBehavior StartupBehavior
    {
        get => _settings.StartupBehavior;
        set
        {
            if (_settings.StartupBehavior != value)
            {
                _settings.StartupBehavior = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StartupSyncPhysicalFaders));
                OnPropertyChanged(nameof(StartupForce100Percent));
                OnPropertyChanged(nameof(StartupUseProfileLevels));
                ProfileManager.SaveSettings(_settings);
            }
        }
    }

    public bool StartupSyncPhysicalFaders
    {
        get => StartupBehavior == StartupLevelBehavior.SyncPhysicalFaders;
        set { if (value) StartupBehavior = StartupLevelBehavior.SyncPhysicalFaders; }
    }

    public bool StartupForce100Percent
    {
        get => StartupBehavior == StartupLevelBehavior.Force100Percent;
        set { if (value) StartupBehavior = StartupLevelBehavior.Force100Percent; }
    }

    public bool StartupUseProfileLevels
    {
        get => StartupBehavior == StartupLevelBehavior.UseProfileSavedLevels;
        set { if (value) StartupBehavior = StartupLevelBehavior.UseProfileSavedLevels; }
    }

    // --- CHANNEL ENDPOINTS COLLECTIONS (MIXER MANAGER) ---
    public IEnumerable<ChannelMixerItemViewModel> InputChannels => AllChannels.Where(c =>
        c.Channel != ChannelName.Headphones &&
        c.Channel != ChannelName.LineOut &&
        c.Channel != ChannelName.MicMonitor);

    public IEnumerable<ChannelMixerItemViewModel> OutputChannels => AllChannels.Where(c =>
        c.Channel == ChannelName.Headphones ||
        c.Channel == ChannelName.LineOut ||
        c.Channel == ChannelName.MicMonitor);

    // --- VOICE SOUND SHAPER ACTIVE SELECTIONS ---
    public bool IsGateSelectedOff => !GateEnabled;
    public bool IsGateSelectedLight => GateEnabled && GateThresholdDb <= -48;
    public bool IsGateSelectedBalanced => GateEnabled && GateThresholdDb > -48 && GateThresholdDb <= -38;
    public bool IsGateSelectedStrict => GateEnabled && GateThresholdDb > -38 && GateThresholdDb <= -30;
    public bool IsGateSelectedMax => GateEnabled && GateThresholdDb > -30;

    public bool IsEqSelectedFlat => !EqEnabled;
    public bool IsEqSelectedCrisp => EqEnabled && _profile.Eq3KHzGain >= 3 && _profile.Eq90HzGain < 3;
    public bool IsEqSelectedBalanced => EqEnabled && _profile.Eq3KHzGain < 3 && _profile.Eq90HzGain < 3;
    public bool IsEqSelectedWarm => EqEnabled && _profile.Eq90HzGain >= 3 && _profile.Eq90HzGain < 5;
    public bool IsEqSelectedDeep => EqEnabled && _profile.Eq90HzGain >= 5;

    public bool IsCompSelectedOff => !CompressorEnabled;
    public bool IsCompSelectedGentle => CompressorEnabled && CompressorThresholdDb >= -16;
    public bool IsCompSelectedBalanced => CompressorEnabled && CompressorThresholdDb < -16 && CompressorThresholdDb >= -18;
    public bool IsCompSelectedBroadcast => CompressorEnabled && CompressorThresholdDb < -18 && CompressorThresholdDb >= -24;
    public bool IsCompSelectedMaxBoost => CompressorEnabled && CompressorThresholdDb < -24;

    public bool IsDeEsserSelectedOff => !DeEsserEnabled || DeEsserPercent == 0;
    public bool IsDeEsserSelectedSoft => DeEsserEnabled && DeEsserPercent > 0 && DeEsserPercent <= 20;
    public bool IsDeEsserSelectedMed => DeEsserEnabled && DeEsserPercent > 20 && DeEsserPercent <= 45;
    public bool IsDeEsserSelectedHigh => DeEsserEnabled && DeEsserPercent > 45;

    public bool IsBleepSelectedQuiet => BleepLevelDb <= -15;
    public bool IsBleepSelectedNormal => BleepLevelDb > -15 && BleepLevelDb <= -6;
    public bool IsBleepSelectedLoud => BleepLevelDb > -6 && BleepLevelDb <= -2;
    public bool IsBleepSelectedMax => BleepLevelDb > -2;

    public void NotifyVoiceShaperProperties()
    {
        OnPropertyChanged(nameof(IsGateSelectedOff));
        OnPropertyChanged(nameof(IsGateSelectedLight));
        OnPropertyChanged(nameof(IsGateSelectedBalanced));
        OnPropertyChanged(nameof(IsGateSelectedStrict));
        OnPropertyChanged(nameof(IsGateSelectedMax));

        OnPropertyChanged(nameof(IsEqSelectedFlat));
        OnPropertyChanged(nameof(IsEqSelectedCrisp));
        OnPropertyChanged(nameof(IsEqSelectedBalanced));
        OnPropertyChanged(nameof(IsEqSelectedWarm));
        OnPropertyChanged(nameof(IsEqSelectedDeep));

        OnPropertyChanged(nameof(IsCompSelectedOff));
        OnPropertyChanged(nameof(IsCompSelectedGentle));
        OnPropertyChanged(nameof(IsCompSelectedBalanced));
        OnPropertyChanged(nameof(IsCompSelectedBroadcast));
        OnPropertyChanged(nameof(IsCompSelectedMaxBoost));

        OnPropertyChanged(nameof(IsDeEsserSelectedOff));
        OnPropertyChanged(nameof(IsDeEsserSelectedSoft));
        OnPropertyChanged(nameof(IsDeEsserSelectedMed));
        OnPropertyChanged(nameof(IsDeEsserSelectedHigh));

        OnPropertyChanged(nameof(IsBleepSelectedQuiet));
        OnPropertyChanged(nameof(IsBleepSelectedNormal));
        OnPropertyChanged(nameof(IsBleepSelectedLoud));
        OnPropertyChanged(nameof(IsBleepSelectedMax));

        NotifyMeterDspRangeProperties();
    }

    public void NotifyMeterDspRangeProperties()
    {
        OnPropertyChanged(nameof(GateThresholdPercentOnMeter));
        OnPropertyChanged(nameof(GateCutoffMeterPercent));
        OnPropertyChanged(nameof(GateCutoffActiveWidth));
        OnPropertyChanged(nameof(GateCutoffRemainingWidth));
        OnPropertyChanged(nameof(GateCutoffLabel));
        OnPropertyChanged(nameof(GateZoneVisibility));
        OnPropertyChanged(nameof(GateSimpleSummaryText));
        OnPropertyChanged(nameof(IsGateOpen));
        OnPropertyChanged(nameof(GateStatusText));
        OnPropertyChanged(nameof(IsAiDoubleSuppressionConflict));

        OnPropertyChanged(nameof(CompressorCatchMeterPercent));
        OnPropertyChanged(nameof(CompressorPreWidth));
        OnPropertyChanged(nameof(CompressorActiveWidth));
        OnPropertyChanged(nameof(CompressorMeterLabel));
        OnPropertyChanged(nameof(CompressorZoneVisibility));
        OnPropertyChanged(nameof(CompressorSimpleSummaryText));

        OnPropertyChanged(nameof(EqSimpleSummaryText));
        OnPropertyChanged(nameof(EqMeterSummaryLabel));
        OnPropertyChanged(nameof(DeEsserSimpleSummaryText));
    }

    // --- HEADPHONE SOUND PROFILE (SENNHEISER HD 560S & AUDIOQUEST DRAGONFLY) ---
    private string _headphonePreset = "Harman";
    private double _headphoneSubBassDb = 3.5;
    private double _headphoneWarmthDb = -0.5;
    private double _headphonePresenceDb = -1.5;
    private double _headphoneAirDb = 1.0;

    public string HeadphonePreset
    {
        get => _headphonePreset;
        set
        {
            if (SetProperty(ref _headphonePreset, value))
            {
                OnPropertyChanged(nameof(IsHeadphoneHarman));
                OnPropertyChanged(nameof(IsHeadphoneBassBoost));
                OnPropertyChanged(nameof(IsHeadphoneGaming));
                OnPropertyChanged(nameof(IsHeadphoneVocalWarm));
                OnPropertyChanged(nameof(IsHeadphoneFlat));
                OnPropertyChanged(nameof(HeadphonePresetSummaryText));
            }
        }
    }

    public bool IsHeadphoneHarman => _headphonePreset == "Harman";
    public bool IsHeadphoneBassBoost => _headphonePreset == "BassBoost";
    public bool IsHeadphoneGaming => _headphonePreset == "Gaming";
    public bool IsHeadphoneVocalWarm => _headphonePreset == "VocalWarm";
    public bool IsHeadphoneFlat => _headphonePreset == "Flat";

    public double HeadphoneSubBassDb
    {
        get => _headphoneSubBassDb;
        set
        {
            double clamped = Math.Clamp(Math.Round(value, 1), -6.0, 8.0);
            if (SetProperty(ref _headphoneSubBassDb, clamped))
            {
                _profile.HeadphoneSubBassDb = clamped;
                _audioMonitorService.ApplyHeadphoneProfile(_headphonePreset, _headphoneSubBassDb, _headphoneWarmthDb, _headphonePresenceDb, _headphoneAirDb);
                OnPropertyChanged(nameof(HeadphoneSubBassLabel));
                AutoSaveIfEnabled();
            }
        }
    }

    public double HeadphoneWarmthDb
    {
        get => _headphoneWarmthDb;
        set
        {
            double clamped = Math.Clamp(Math.Round(value, 1), -6.0, 6.0);
            if (SetProperty(ref _headphoneWarmthDb, clamped))
            {
                _profile.HeadphoneWarmthDb = clamped;
                _audioMonitorService.ApplyHeadphoneProfile(_headphonePreset, _headphoneSubBassDb, _headphoneWarmthDb, _headphonePresenceDb, _headphoneAirDb);
                OnPropertyChanged(nameof(HeadphoneWarmthLabel));
                AutoSaveIfEnabled();
            }
        }
    }

    public double HeadphonePresenceDb
    {
        get => _headphonePresenceDb;
        set
        {
            double clamped = Math.Clamp(Math.Round(value, 1), -6.0, 6.0);
            if (SetProperty(ref _headphonePresenceDb, clamped))
            {
                _profile.HeadphonePresenceDb = clamped;
                _audioMonitorService.ApplyHeadphoneProfile(_headphonePreset, _headphoneSubBassDb, _headphoneWarmthDb, _headphonePresenceDb, _headphoneAirDb);
                OnPropertyChanged(nameof(HeadphonePresenceLabel));
                AutoSaveIfEnabled();
            }
        }
    }

    public double HeadphoneAirDb
    {
        get => _headphoneAirDb;
        set
        {
            double clamped = Math.Clamp(Math.Round(value, 1), -6.0, 6.0);
            if (SetProperty(ref _headphoneAirDb, clamped))
            {
                _profile.HeadphoneAirDb = clamped;
                _audioMonitorService.ApplyHeadphoneProfile(_headphonePreset, _headphoneSubBassDb, _headphoneWarmthDb, _headphonePresenceDb, _headphoneAirDb);
                OnPropertyChanged(nameof(HeadphoneAirLabel));
                AutoSaveIfEnabled();
            }
        }
    }

    public string HeadphoneSubBassLabel => $"{(_headphoneSubBassDb >= 0 ? "+" : "")}{_headphoneSubBassDb:F1} dB";
    public string HeadphoneWarmthLabel => $"{(_headphoneWarmthDb >= 0 ? "+" : "")}{_headphoneWarmthDb:F1} dB";
    public string HeadphonePresenceLabel => $"{(_headphonePresenceDb >= 0 ? "+" : "")}{_headphonePresenceDb:F1} dB";
    public string HeadphoneAirLabel => $"{(_headphoneAirDb >= 0 ? "+" : "")}{_headphoneAirDb:F1} dB";

    // Dynamic Context-Aware Headphone EQ Terminology
    public string HeadphoneBand0Title => _headphonePreset switch
    {
        "Gaming" => "💥 Explosion & Vehicle Rumble (80 Hz)",
        "Harman" => "📉 Sub-Bass Extension (80 Hz Low-Shelf)",
        "BassBoost" => "🔊 Sub-Bass Depth & Rumble (80 Hz)",
        "VocalWarm" => "🎻 Acoustic Lows & Body (80 Hz)",
        "Flat" => "📊 Low Shelf Filter (80 Hz)",
        _ => "Sub-Bass & Deep Rumble (80 Hz)"
    };

    public string HeadphoneBand0Hint => _headphonePreset switch
    {
        "Gaming" => "Tames booming grenades, artillery, and vehicle engines to prevent ear fatigue and audio masking.",
        "Harman" => "Linear sub-bass shelf compensating for open-back acoustic roll-off down to 20 Hz.",
        "BassBoost" => "Visceral sub-bass rumble for EDM drops, 808s, and cinematic film impact.",
        "VocalWarm" => "Gentle low-end foundation preserving clarity for acoustic instruments.",
        "Flat" => "Standard 80 Hz low-shelf filter (0 dB bit-perfect bypass).",
        _ => "Controls deepest bass frequencies below 80 Hz."
    };

    public string HeadphoneBand1Title => _headphonePreset switch
    {
        "Gaming" => "🎯 Gunshot Punch & Weapon Body (350 Hz)",
        "Harman" => "⚖️ Lower Midrange Neutrality (350 Hz)",
        "BassBoost" => "🎸 Warm Bass Punch & Kick (350 Hz)",
        "VocalWarm" => "🎙️ Vocal Chest & Proximity Warmth (350 Hz)",
        "Flat" => "📊 Lower Mid Peaking Filter (350 Hz, Q=0.8)",
        _ => "Warmth & Vocal Chest (350 Hz)"
    };

    public string HeadphoneBand1Hint => _headphonePreset switch
    {
        "Gaming" => "Crisp weapon cycling, mag reloads, and gunshot body without muddy room reverberation.",
        "Harman" => "Balanced lower harmonic warmth for true-to-source vocal and instrumental timbre.",
        "BassBoost" => "Punchy kick drums and driving bass guitar presence.",
        "VocalWarm" => "Rich radio-broadcast chest resonance and intimate warmth for dialogue.",
        "Flat" => "Parametric peaking bell filter targeting lower midrange frequencies.",
        _ => "Adds warmth to voices and acoustic instruments."
    };

    public string HeadphoneBand2Title => _headphonePreset switch
    {
        "Gaming" => "👟 Enemy Footsteps & Tactical Cues (3.2 kHz)",
        "Harman" => "🛡️ Upper-Mid Taming (Smooth 5.6 kHz Peak)",
        "BassBoost" => "✨ Lead Vocal & Synth Articulation (3.2 kHz)",
        "VocalWarm" => "🗣️ Vocal Intimacy & Presence (3.2 kHz)",
        "Flat" => "📊 Upper Mid Peaking Filter (3.2 kHz, Q=1.0)",
        _ => "Presence & FPS Footsteps (3.2 kHz)"
    };

    public string HeadphoneBand2Hint => _headphonePreset switch
    {
        "Gaming" => "Surgical tactical boost for enemy footsteps on wood/gravel, pin pulls, and armor plates.",
        "Harman" => "Smooths the Sennheiser HD 560S upper-mid resonance peak for fatigue-free reference listening.",
        "BassBoost" => "Keeps vocals and lead instruments cutting through thick, heavy basslines.",
        "VocalWarm" => "Brings dialogue forward with velvety clarity and zero harshness.",
        "Flat" => "Parametric peaking filter targeting human hearing sensitivity.",
        _ => "Controls voice intelligibility and hearing sensitivity."
    };

    public string HeadphoneBand3Title => _headphonePreset switch
    {
        "Gaming" => "🏹 Spatial Direction & Bullet Whistles (8.5 kHz)",
        "Harman" => "💨 Treble Air & Open Dispersion (8.5 kHz)",
        "BassBoost" => "⚡ Hi-Hat & Cymbal Crispness (8.5 kHz)",
        "VocalWarm" => "🕊️ Velvet Air & Sibilance Roll-off (8.5 kHz)",
        "Flat" => "📊 High Shelf Filter (8.5 kHz)",
        _ => "Air & Acoustic Sparkle (8.5 kHz)"
    };

    public string HeadphoneBand3Hint => _headphonePreset switch
    {
        "Gaming" => "Pinpoints 3D directional positioning, bullet whiz-bys, and environmental cues.",
        "Harman" => "Ultra-smooth treble air opening up the HD 560S soundstage without piercing brightness.",
        "BassBoost" => "Sharp cymbal definition and crisp snare attack.",
        "VocalWarm" => "Silky-smooth top-end roll-off that tames harsh 's' and 't' sounds on long sessions.",
        "Flat" => "Standard 8.5 kHz high-shelf filter for top-end air control.",
        _ => "Adds open-air dimension and top-end sparkle."
    };

    public bool IsGoXlrDefaultWindowsAudio
    {
        get
        {
            try
            {
                using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var def = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                return def.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                       def.FriendlyName.Contains("System", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    public string WindowsAudioRoutingStatusText =>
        "⚡ Direct 0ms Native Mode Active (Games & Windows audio stream directly to your DAC with zero latency and zero buffering)";

    public void SetGoXlrAsDefaultWindowsAudio()
    {
        WindowsAudioEndpointManager.SetGoXlrChatMicAsDefaultRecording();
        StatusBannerMessage = "⚡ Direct 0ms Mode: Playback goes straight to DAC, Default Mic is Chat Mic (AI Denoised).";
        OnPropertyChanged(nameof(IsGoXlrDefaultWindowsAudio));
        OnPropertyChanged(nameof(WindowsAudioRoutingStatusText));
        _audioMonitorService.StopMonitoring();
    }

    public string HeadphonePresetSummaryText => _headphonePreset switch
    {
        "Harman" => "Harman Neutral (HD 560S Reference: Linear Sub-Bass & Smooth Air)",
        "BassBoost" => "Punchy Bass & Warmth (EDM, Hip-Hop & Cinematic Impact)",
        "Gaming" => "FPS Spatial Footsteps (Tactical Footsteps & Gunshot Imaging)",
        "VocalWarm" => "Velvet Vocal & Acoustic (Lush Body & Fatigue-Free Highs)",
        "Flat" => "Studio Reference Flat (0 dB Bit-Perfect DAC Bypass)",
        _ => "Custom Sound Profile"
    };

    public void SetHeadphonePreset(string preset)
    {
        HeadphonePreset = preset;
        switch (preset)
        {
            case "Harman":
                _headphoneSubBassDb = 3.5;
                _headphoneWarmthDb = -0.5;
                _headphonePresenceDb = -1.5;
                _headphoneAirDb = 1.0;
                StatusBannerMessage = "Headphone Profile: Harman Target Curve (Sennheiser HD 560S calibrated).";
                break;
            case "BassBoost":
                _headphoneSubBassDb = 5.5;
                _headphoneWarmthDb = 2.0;
                _headphonePresenceDb = 0.0;
                _headphoneAirDb = -0.5;
                StatusBannerMessage = "Headphone Profile: Punchy Bass & Warmth applied.";
                break;
            case "Gaming":
                _headphoneSubBassDb = -2.5;
                _headphoneWarmthDb = -1.0;
                _headphonePresenceDb = 4.5;
                _headphoneAirDb = 2.5;
                StatusBannerMessage = "Headphone Profile: FPS Gaming Footsteps & Tactical Spatial Clarity active.";
                break;
            case "VocalWarm":
                _headphoneSubBassDb = 1.5;
                _headphoneWarmthDb = 2.5;
                _headphonePresenceDb = 0.5;
                _headphoneAirDb = -2.0;
                StatusBannerMessage = "Headphone Profile: Velvet Vocal & Acoustic active.";
                break;
            case "Flat":
                _headphoneSubBassDb = 0.0;
                _headphoneWarmthDb = 0.0;
                _headphonePresenceDb = 0.0;
                _headphoneAirDb = 0.0;
                StatusBannerMessage = "Headphone Profile: Pure Studio Reference Flat (Bit-Perfect Bypass).";
                break;
        }

        _profile.HeadphonePreset = preset;
        _profile.HeadphoneSubBassDb = _headphoneSubBassDb;
        _profile.HeadphoneWarmthDb = _headphoneWarmthDb;
        _profile.HeadphonePresenceDb = _headphonePresenceDb;
        _profile.HeadphoneAirDb = _headphoneAirDb;

        OnPropertyChanged(nameof(HeadphoneSubBassDb));
        OnPropertyChanged(nameof(HeadphoneWarmthDb));
        OnPropertyChanged(nameof(HeadphonePresenceDb));
        OnPropertyChanged(nameof(HeadphoneAirDb));
        OnPropertyChanged(nameof(HeadphoneSubBassLabel));
        OnPropertyChanged(nameof(HeadphoneWarmthLabel));
        OnPropertyChanged(nameof(HeadphonePresenceLabel));
        OnPropertyChanged(nameof(HeadphoneAirLabel));

        OnPropertyChanged(nameof(HeadphoneBand0Title));
        OnPropertyChanged(nameof(HeadphoneBand0Hint));
        OnPropertyChanged(nameof(HeadphoneBand1Title));
        OnPropertyChanged(nameof(HeadphoneBand1Hint));
        OnPropertyChanged(nameof(HeadphoneBand2Title));
        OnPropertyChanged(nameof(HeadphoneBand2Hint));
        OnPropertyChanged(nameof(HeadphoneBand3Title));
        OnPropertyChanged(nameof(HeadphoneBand3Hint));

        _audioMonitorService.ApplyHeadphoneProfile(_headphonePreset, _headphoneSubBassDb, _headphoneWarmthDb, _headphonePresenceDb, _headphoneAirDb);
        if (!_audioMonitorService.IsMonitoring)
        {
            EnsureHeadphoneMonitoring();
        }
        AutoSaveIfEnabled();
    }

    // --- LEGACY GOXLR APP CLEANUP ---
    private bool _isLegacyGoXlrAppPresent;

    public bool IsLegacyGoXlrAppPresent => _isLegacyGoXlrAppPresent;

    private static bool DetectLegacyGoXlrAppPresent()
    {
        if (Process.GetProcessesByName("GoXLR App").Length > 0)
        {
            return true;
        }

        string[] candidateExes =
        [
            @"C:\Program Files (x86)\TC-Helicon\GoXLR\GoXLR App.exe",
            @"C:\Program Files\TC-Helicon\GoXLR\GoXLR App.exe",
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"TC-Helicon\GoXLR\GoXLR App.exe")
        ];
        foreach (string exePath in candidateExes)
        {
            if (System.IO.File.Exists(exePath))
            {
                return true;
            }
        }

        const string uninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        const string wowUninstallPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
        (RegistryKey Root, string SubKey)[] registryRoots =
        [
            (Registry.LocalMachine, uninstallPath),
            (Registry.LocalMachine, wowUninstallPath),
            (Registry.CurrentUser, uninstallPath)
        ];

        foreach (var (root, subPath) in registryRoots)
        {
            try
            {
                using var uninstallKey = root.OpenSubKey(subPath);
                if (uninstallKey == null) continue;
                foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                {
                    try
                    {
                        using var appKey = uninstallKey.OpenSubKey(subKeyName);
                        if (appKey?.GetValue("DisplayName") is string displayName &&
                            displayName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                            !displayName.Contains("Driver", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return false;
    }

    public void RefreshLegacyGoXlrStatus()
    {
        _isLegacyGoXlrAppPresent = DetectLegacyGoXlrAppPresent();
        OnPropertyChanged(nameof(IsLegacyGoXlrAppPresent));
        OnPropertyChanged(nameof(LegacyGoXlrStatusText));
    }

    public string LegacyGoXlrStatusText
    {
        get
        {
            var procs = Process.GetProcessesByName("GoXLR App");
            if (procs.Length > 0)
            {
                return "⚠️ Legacy 'GoXLR App' is running in the background. It is safe to close and uninstall it.";
            }
            return "✅ Legacy GoXLR App is closed. Project XLR has 100% direct native USB control.";
        }
    }

    public void KillLegacyGoXlrProcess()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("GoXLR App"))
            {
                p.Kill();
            }
            StatusBannerMessage = "Legacy GoXLR App terminated. Project XLR is managing hardware.";
            RefreshLegacyGoXlrStatus();
        }
        catch (Exception ex)
        {
            StatusBannerMessage = $"Could not close legacy app: {ex.Message}";
        }
    }

    public void OpenWindowsAppsFeatures()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
        }
        catch { }
    }

    private const string RunRegPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunRegValueName = "ProjectXLR";

    public bool RunOnWindowsStartup
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegPath, false);
                return key?.GetValue(RunRegValueName) is string s && !string.IsNullOrWhiteSpace(s);
            }
            catch
            {
                return false;
            }
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegPath, true);
                if (key == null) return;
                if (value)
                {
                    string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
                    key.SetValue(RunRegValueName, $"\"{exePath}\"");
                    StatusBannerMessage = "Project XLR enabled for Windows Startup.";
                }
                else
                {
                    key.DeleteValue(RunRegValueName, false);
                    StatusBannerMessage = "Project XLR removed from Windows Startup.";
                }
                OnPropertyChanged();
            }
            catch (Exception ex)
            {
                StatusBannerMessage = $"Failed to update Windows Startup: {ex.Message}";
            }
        }
    }

    // Profiles
    public string SelectedProfileName
    {
        get => _selectedProfileName;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (SetProperty(ref _selectedProfileName, value))
            {
                LoadAndApplyProfile(value);
            }
        }
    }

    public string NewProfileName
    {
        get => _newProfileName;
        set => SetProperty(ref _newProfileName, value);
    }

    // Input Channel Enables (by default only Microphone is active)
    public bool IsMicInputEnabled
    {
        get => true; // Always active
        set { }
    }

    public bool IsSystemInputEnabled
    {
        get => !_profile.DisabledInputs.Contains(BasicInputDevice.System);
        set => SetInputEnabled(BasicInputDevice.System, value);
    }

    public bool IsGameInputEnabled
    {
        get => !_profile.DisabledInputs.Contains(BasicInputDevice.Game);
        set => SetInputEnabled(BasicInputDevice.Game, value);
    }

    public bool IsMusicInputEnabled
    {
        get => !_profile.DisabledInputs.Contains(BasicInputDevice.Music);
        set => SetInputEnabled(BasicInputDevice.Music, value);
    }

    public bool IsChatInputEnabled
    {
        get => !_profile.DisabledInputs.Contains(BasicInputDevice.Chat);
        set => SetInputEnabled(BasicInputDevice.Chat, value);
    }

    public bool IsLineInInputEnabled
    {
        get => !_profile.DisabledInputs.Contains(BasicInputDevice.LineIn);
        set => SetInputEnabled(BasicInputDevice.LineIn, value);
    }

    public bool IsConsoleInputEnabled
    {
        get => !_profile.DisabledInputs.Contains(BasicInputDevice.Console);
        set => SetInputEnabled(BasicInputDevice.Console, value);
    }

    public bool IsSamplesInputEnabled
    {
        get => !_profile.DisabledInputs.Contains(BasicInputDevice.Samples);
        set => SetInputEnabled(BasicInputDevice.Samples, value);
    }

    // Output Channel Enables (by default only BroadcastMix is active)
    public bool IsHeadphonesOutputEnabled
    {
        get => !_profile.DisabledOutputs.Contains(BasicOutputDevice.Headphones);
        set => SetOutputEnabled(BasicOutputDevice.Headphones, value);
    }

    public bool IsBroadcastOutputEnabled
    {
        get => !_profile.DisabledOutputs.Contains(BasicOutputDevice.BroadcastMix);
        set => SetOutputEnabled(BasicOutputDevice.BroadcastMix, value);
    }

    public bool IsChatMicOutputEnabled
    {
        get => !_profile.DisabledOutputs.Contains(BasicOutputDevice.ChatMic);
        set => SetOutputEnabled(BasicOutputDevice.ChatMic, value);
    }

    public bool IsSamplerOutputEnabled
    {
        get => !_profile.DisabledOutputs.Contains(BasicOutputDevice.Sampler);
        set => SetOutputEnabled(BasicOutputDevice.Sampler, value);
    }

    public bool IsLineOutOutputEnabled
    {
        get => !_profile.DisabledOutputs.Contains(BasicOutputDevice.LineOut);
        set => SetOutputEnabled(BasicOutputDevice.LineOut, value);
    }

    public string EnabledChannelsSummaryText
    {
        get
        {
            int inputs = 8 - _profile.DisabledInputs.Count;
            int outputs = 5 - _profile.DisabledOutputs.Count;
            return $"{inputs} Input{(inputs == 1 ? "" : "s")}, {outputs} Output{(outputs == 1 ? "" : "s")} active";
        }
    }

    public void NotifyChannelEnabledProperties()
    {
        OnPropertyChanged(nameof(IsMicInputEnabled));
        OnPropertyChanged(nameof(IsSystemInputEnabled));
        OnPropertyChanged(nameof(IsGameInputEnabled));
        OnPropertyChanged(nameof(IsMusicInputEnabled));
        OnPropertyChanged(nameof(IsChatInputEnabled));
        OnPropertyChanged(nameof(IsLineInInputEnabled));
        OnPropertyChanged(nameof(IsConsoleInputEnabled));
        OnPropertyChanged(nameof(IsSamplesInputEnabled));
        OnPropertyChanged(nameof(IsBroadcastOutputEnabled));
        OnPropertyChanged(nameof(IsHeadphonesOutputEnabled));
        OnPropertyChanged(nameof(IsLineOutOutputEnabled));
        OnPropertyChanged(nameof(IsChatMicOutputEnabled));
        OnPropertyChanged(nameof(IsSamplerOutputEnabled));
        OnPropertyChanged(nameof(EnabledChannelsSummaryText));
        RaiseRoutingGraphChanged();
    }

    public void ResetToMinimalChannels()
    {
        _profile.DisabledInputs.Clear();
        _profile.DisabledOutputs.Clear();

        // 1 Mic, 1 Game/System, 1 Chat (Discord), 1 Headphones (Master Output)
        _profile.FaderAssignments[FaderName.A] = ChannelName.Mic;
        _profile.FaderAssignments[FaderName.B] = ChannelName.System;
        _profile.FaderAssignments[FaderName.C] = ChannelName.Chat;
        _profile.FaderAssignments[FaderName.D] = ChannelName.Headphones;

        foreach (var fader in Enum.GetValues<FaderName>())
        {
            var ch = _profile.FaderAssignments[fader];
            _device.SetFaderChannel(fader, ch);
            _device.SetFaderDisplayMode(fader, gradient: false, meter: false);
        }

        foreach (var inp in Enum.GetValues<BasicInputDevice>())
        {
            PushRoutingForInput(inp);
        }

        WindowsAudioEndpointManager.EnsureAllGoXlrEndpointsActive();
        WindowsAudioEndpointManager.SetGoXlrSystemAsDefaultPlayback();
        WindowsAudioEndpointManager.SetGoXlrChatAsDefaultCommunicationsPlayback();
        WindowsAudioEndpointManager.SetGoXlrChatMicAsDefaultRecording();

        SyncViewModelsFromProfile();
        NotifyChannelEnabledProperties();
        AutoSaveIfEnabled();
        StatusBannerMessage = "⚡ Streamlined Setup Active: Slider 1=Mic, Slider 2=System/Desktop, Slider 3=Chat (Discord), Slider 4=Headphones Master.";
    }

    public void EnableStandardChannels()
    {
        _profile.DisabledInputs.Remove(BasicInputDevice.System);
        _profile.DisabledInputs.Remove(BasicInputDevice.Music);
        _profile.DisabledInputs.Remove(BasicInputDevice.Game);
        _profile.DisabledOutputs.Remove(BasicOutputDevice.Headphones);
        _profile.DisabledOutputs.Remove(BasicOutputDevice.BroadcastMix);

        foreach (var inp in Enum.GetValues<BasicInputDevice>())
        {
            PushRoutingForInput(inp);
        }

        WindowsAudioEndpointManager.SyncAllGoXlrEndpoints(_profile);

        SyncViewModelsFromProfile();
        NotifyChannelEnabledProperties();
        AutoSaveIfEnabled();
        StatusBannerMessage = "🎛️ Standard Channels Enabled: Mic, System, Game, Music & Broadcast Mix.";
    }

    public void EnableAllChannels()
    {
        _profile.DisabledInputs.Clear();
        _profile.DisabledOutputs.Clear();

        foreach (var inp in Enum.GetValues<BasicInputDevice>())
        {
            PushRoutingForInput(inp);
        }

        WindowsAudioEndpointManager.SyncAllGoXlrEndpoints(_profile);

        SyncViewModelsFromProfile();
        NotifyChannelEnabledProperties();
        AutoSaveIfEnabled();
        StatusBannerMessage = "🚀 All Virtual Audio Channels & Endpoints are now enabled and visible.";
    }

    public void SyncWindowsSoundEndpoints()
    {
        WindowsAudioEndpointManager.SyncAllGoXlrEndpoints(_profile);
        WindowsAudioEndpointManager.SetGoXlrChatMicAsDefaultRecording();
        StatusBannerMessage = "⚡ Windows Sound Devices synced! Chat Mic set as default voice input and unused channels hidden.";
    }

    public void OpenWindowsSoundControlPanel()
    {
        WindowsAudioEndpointManager.OpenWindowsSoundControlPanel();
    }

    public void EnableFaderChannel(FaderName fader)
    {
        var ch = _profile.FaderAssignments.GetValueOrDefault(fader, ChannelName.Mic);
        SetChannelEnabled(ch, true);
        NotifyChannelEnabledProperties();
    }

    // Mic & Preamp
    public MicrophoneType MicType
    {
        get => _profile.MicType;
        set
        {
            if (_profile.MicType != value)
            {
                _profile.MicType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDynamicMic));
                OnPropertyChanged(nameof(IsCondenserMic));
                OnPropertyChanged(nameof(IsJackMic));
                OnPropertyChanged(nameof(MicGainDb));
                OnPropertyChanged(nameof(MicGainActiveWidth));
                OnPropertyChanged(nameof(MicGainRemainingWidth));
                ApplyMicGainToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public bool IsDynamicMic
    {
        get => _profile.MicType == MicrophoneType.Dynamic;
        set { if (value) MicType = MicrophoneType.Dynamic; }
    }

    public bool IsCondenserMic
    {
        get => _profile.MicType == MicrophoneType.Condenser;
        set { if (value) MicType = MicrophoneType.Condenser; }
    }

    public bool IsJackMic
    {
        get => _profile.MicType == MicrophoneType.Jack;
        set { if (value) MicType = MicrophoneType.Jack; }
    }

    public int MicGainDb
    {
        get => _profile.GetActiveMicGainDb();
        set
        {
            ushort clamped = (ushort)Math.Clamp(value, 0, 72);
            if (_profile.GetActiveMicGainDb() != clamped)
            {
                _profile.SetActiveMicGainDb(clamped);
                OnPropertyChanged();
                OnPropertyChanged(nameof(MicGainActiveWidth));
                OnPropertyChanged(nameof(MicGainRemainingWidth));
                ApplyMicGainToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public GridLength MicGainActiveWidth => new(Math.Max(0.001, MicGainDb / 72.0 * 100.0), GridUnitType.Star);
    public GridLength MicGainRemainingWidth => new(Math.Max(0.001, 100.0 - (MicGainDb / 72.0 * 100.0)), GridUnitType.Star);

    public void AdjustMicGain(int deltaDb) => MicGainDb = Math.Clamp(MicGainDb + deltaDb, 0, 72);
    public void SetMicGainPreset(int gainDb) => MicGainDb = Math.Clamp(gainDb, 0, 72);

    // Unified Sidetone (Mic Monitor in Headphones)
    public bool SidetoneEnabled
    {
        get => !_profile.MicMonitorDisabled && !_profile.ChannelMuted.GetValueOrDefault(ChannelName.MicMonitor, false);
        set
        {
            if (SidetoneEnabled != value)
            {
                SetSidetoneState(value, value ? "Sidetone (Hear Myself) Enabled" : "Sidetone (Hear Myself) Muted");
            }
        }
    }

    public string SidetoneStatusText => SidetoneEnabled ? $"Sidetone {SidetoneVolumePercent}%" : "Sidetone Off";

    public GridLength SidetoneActiveWidth => new(Math.Max(0.001, SidetoneVolumePercent), GridUnitType.Star);
    public GridLength SidetoneRemainingWidth => new(Math.Max(0.001, 100.0 - SidetoneVolumePercent), GridUnitType.Star);

    public void AdjustSidetoneVolume(int deltaPercent) => SidetoneVolumePercent = Math.Clamp(SidetoneVolumePercent + deltaPercent, 0, 100);
    public void SetSidetonePreset(int percent)
    {
        if (!SidetoneEnabled && percent > 0) SidetoneEnabled = true;
        SidetoneVolumePercent = Math.Clamp(percent, 0, 100);
    }

    public int SidetoneVolumePercent
    {
        get => (int)Math.Round(_profile.ChannelVolumes.GetValueOrDefault(ChannelName.MicMonitor, (byte)200) / 255.0 * 100.0);
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            byte raw = (byte)Math.Round(clamped / 100.0 * 255.0);
            if (_profile.ChannelVolumes.GetValueOrDefault(ChannelName.MicMonitor, (byte)200) != raw)
            {
                _profile.ChannelVolumes[ChannelName.MicMonitor] = raw;
                UpdateMicMonitor();

                foreach (var f in Enum.GetValues<FaderName>())
                {
                    if (_profile.FaderAssignments.GetValueOrDefault(f) == ChannelName.MicMonitor)
                    {
                        _faderPaused[(int)f] = true;
                        _faderPauseReferenceHwVol[(int)f] = _faderLastSeen[(int)f];
                    }
                }

                if (SidetoneEnabled)
                {
                    _device.SetChannelVolume(ChannelName.MicMonitor, raw);
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(SidetoneStatusText));
                OnPropertyChanged(nameof(SidetoneActiveWidth));
                OnPropertyChanged(nameof(SidetoneRemainingWidth));

                foreach (var chVm in AllChannels)
                {
                    if (chVm.Channel == ChannelName.MicMonitor)
                    {
                        chVm.SetFromHardwareOrProfile(raw, chVm.IsMuted, chVm.IsDisabled);
                    }
                }

                foreach (var slot in FaderSlots)
                {
                    if (slot.AssignedChannel == ChannelName.MicMonitor)
                    {
                        slot.SyncSilent(ChannelName.MicMonitor, raw, slot.IsMuted, slot.IsChannelEnabled);
                    }
                }

                AutoSaveIfEnabled();
            }
        }
    }

    public string AccentHexColor
    {
        get => _profile.AccentHexColor;
        set
        {
            string clean = CleanHex(value, "00FFFF");
            if (_profile.AccentHexColor != clean)
            {
                ApplyAccentColour(clean);
                OnPropertyChanged();
            }
        }
    }

    public string FaderAColor
    {
        get => _profile.FaderAColor;
        set
        {
            string clean = CleanHex(value, "00FFFF");
            if (_profile.FaderAColor != clean)
            {
                _profile.FaderAColor = clean;
                _profile.LightingEffect = "Custom";
                OnPropertyChanged();
                OnPropertyChanged(nameof(FaderABrush));
                OnPropertyChanged(nameof(ActiveLightingSummary));
                OnPropertyChanged(nameof(SelectedFaderColorChipBrush));
                RefreshHardwareLighting();
                AutoSaveIfEnabled();
            }
        }
    }

    public string FaderBColor
    {
        get => _profile.FaderBColor;
        set
        {
            string clean = CleanHex(value, "0A84FF");
            if (_profile.FaderBColor != clean)
            {
                _profile.FaderBColor = clean;
                _profile.LightingEffect = "Custom";
                OnPropertyChanged();
                OnPropertyChanged(nameof(FaderBBrush));
                OnPropertyChanged(nameof(ActiveLightingSummary));
                OnPropertyChanged(nameof(SelectedFaderColorChipBrush));
                RefreshHardwareLighting();
                AutoSaveIfEnabled();
            }
        }
    }

    public string FaderCColor
    {
        get => _profile.FaderCColor;
        set
        {
            string clean = CleanHex(value, "A855F7");
            if (_profile.FaderCColor != clean)
            {
                _profile.FaderCColor = clean;
                _profile.LightingEffect = "Custom";
                OnPropertyChanged();
                OnPropertyChanged(nameof(FaderCBrush));
                OnPropertyChanged(nameof(ActiveLightingSummary));
                OnPropertyChanged(nameof(SelectedFaderColorChipBrush));
                RefreshHardwareLighting();
                AutoSaveIfEnabled();
            }
        }
    }

    public string FaderDColor
    {
        get => _profile.FaderDColor;
        set
        {
            string clean = CleanHex(value, "FF2D55");
            if (_profile.FaderDColor != clean)
            {
                _profile.FaderDColor = clean;
                _profile.LightingEffect = "Custom";
                OnPropertyChanged();
                OnPropertyChanged(nameof(FaderDBrush));
                OnPropertyChanged(nameof(ActiveLightingSummary));
                OnPropertyChanged(nameof(SelectedFaderColorChipBrush));
                RefreshHardwareLighting();
                AutoSaveIfEnabled();
            }
        }
    }

    public SolidColorBrush FaderABrush => HexToBrush(_profile.FaderAColor);
    public SolidColorBrush FaderBBrush => HexToBrush(_profile.FaderBColor);
    public SolidColorBrush FaderCBrush => HexToBrush(_profile.FaderCColor);
    public SolidColorBrush FaderDBrush => HexToBrush(_profile.FaderDColor);

    public int SelectedFaderLightingIndex
    {
        get => _selectedFaderLightingIndex;
        set
        {
            if (SetProperty(ref _selectedFaderLightingIndex, Math.Clamp(value, 0, 4)))
            {
                OnPropertyChanged(nameof(SelectedFaderLightingTitle));
                OnPropertyChanged(nameof(SelectedFaderColorChipBrush));
                OnPropertyChanged(nameof(IsFaderASelectedForLighting));
                OnPropertyChanged(nameof(IsFaderBSelectedForLighting));
                OnPropertyChanged(nameof(IsFaderCSelectedForLighting));
                OnPropertyChanged(nameof(IsFaderDSelectedForLighting));
                OnPropertyChanged(nameof(IsAllFadersSelectedForLighting));
            }
        }
    }

    public bool IsFaderASelectedForLighting => _selectedFaderLightingIndex == 0;
    public bool IsFaderBSelectedForLighting => _selectedFaderLightingIndex == 1;
    public bool IsFaderCSelectedForLighting => _selectedFaderLightingIndex == 2;
    public bool IsFaderDSelectedForLighting => _selectedFaderLightingIndex == 3;
    public bool IsAllFadersSelectedForLighting => _selectedFaderLightingIndex == 4;

    public string SelectedFaderLightingTitle => _selectedFaderLightingIndex switch
    {
        0 => $"Fader A • {DspTables.GetChannelDisplayName(_profile.FaderAssignments.GetValueOrDefault(FaderName.A, ChannelName.Mic))}",
        1 => $"Fader B • {DspTables.GetChannelDisplayName(_profile.FaderAssignments.GetValueOrDefault(FaderName.B, ChannelName.Music))}",
        2 => $"Fader C • {DspTables.GetChannelDisplayName(_profile.FaderAssignments.GetValueOrDefault(FaderName.C, ChannelName.Chat))}",
        3 => $"Fader D • {DspTables.GetChannelDisplayName(_profile.FaderAssignments.GetValueOrDefault(FaderName.D, ChannelName.System))}",
        _ => "All 4 Faders (Global Desk)"
    };

    public SolidColorBrush SelectedFaderColorChipBrush => _selectedFaderLightingIndex switch
    {
        0 => FaderABrush,
        1 => FaderBBrush,
        2 => FaderCBrush,
        3 => FaderDBrush,
        _ => HexToBrush(_profile.AccentHexColor)
    };

    public string CustomHexInput
    {
        get => _customHexInput;
        set => SetProperty(ref _customHexInput, value);
    }

    public string ActiveLightingSummary => _profile.LightingEffect switch
    {
        "Solid" => $"Solid #{_profile.AccentHexColor}",
        "Custom" => "Custom Per-Fader Colors",
        "Cyberpunk" => "Cyberpunk Neon (Cyan • Blue • Purple • Pink)",
        "Sunset" => "Sunset Glow (Violet • Pink • Orange • Gold)",
        "Ocean" => "Ocean Wave (Navy • Blue • Teal • Mint)",
        "FireIce" => "Fire & Ice (Ice Cyan • Blue • Orange • Crimson)",
        "Matrix" => "Matrix Cyber Green (Emerald • Mint • Lime)",
        "Monochrome" => "Monochrome Minimal (Slate • Silver • White)",
        "VoiceReactive" => "Voice-Reactive VU Meter",
        "Breathing" => $"Breathing Pulse (#{_profile.AccentHexColor})",
        "RetroRainbow" => "Retro Rainbow Wave",
        "RainbowBright" => "Rainbow Bright",
        "RainbowDark" => "Rainbow Dark",
        "WaterfallDown" => "Waterfall Down",
        "WaterfallUp" => "Waterfall Up",
        _ => _profile.LightingEffect
    };

    public string DeviceModelText => _device.IsConnected ? _device.DeviceModel : "Disconnected";
    public string DeviceFirmwareText => _device.IsConnected ? $"v{_device.FirmwareVersion}" : "—";
    public string DeviceSerialText => _device.IsConnected ? _device.SerialNumber : "—";

    // Noise Gate
    public bool GateEnabled
    {
        get => _profile.GateEnabled;
        set
        {
            if (_profile.GateEnabled != value)
            {
                _profile.GateEnabled = value;
                OnPropertyChanged();
                NotifyVoiceShaperProperties();
                ApplyNoiseGateToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public bool GateSimpleMode
    {
        get => !_profile.GateAdvancedMode;
        set { if (value) GateAdvancedMode = false; }
    }

    public bool GateAdvancedMode
    {
        get => _profile.GateAdvancedMode;
        set
        {
            if (_profile.GateAdvancedMode != value)
            {
                _profile.GateAdvancedMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GateSimpleMode));
                OnPropertyChanged(nameof(GateSimpleVisibility));
                OnPropertyChanged(nameof(GateAdvancedVisibility));
                AutoSaveIfEnabled();
            }
        }
    }

    public Visibility GateSimpleVisibility => _profile.GateAdvancedMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GateAdvancedVisibility => _profile.GateAdvancedMode ? Visibility.Visible : Visibility.Collapsed;

    public int GateSimpleAmount
    {
        get => _profile.GateSimpleAmount;
        set
        {
            int c = Math.Clamp(value, 0, 100);
            if (_profile.GateSimpleAmount != c)
            {
                _profile.GateSimpleAmount = c;
                _profile.GateThresholdDb = (int)Math.Round(-59.0 + (c * 0.53));
                _profile.GateAttenuationPercent = Math.Clamp(60 + (c * 40 / 100), 0, 100);
                OnPropertyChanged();
                OnPropertyChanged(nameof(GateThresholdDb));
                OnPropertyChanged(nameof(GateAttenuationPercent));
                NotifyVoiceShaperProperties();
                ApplyNoiseGateToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public int GateThresholdDb
    {
        get => _profile.GateThresholdDb;
        set
        {
            int c = Math.Clamp(value, -59, 0);
            if (_profile.GateThresholdDb != c)
            {
                _profile.GateThresholdDb = c;
                OnPropertyChanged();
                NotifyVoiceShaperProperties();
                ApplyNoiseGateToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public double GateThresholdPercentOnMeter => GateEnabled ? Math.Clamp((_profile.GateThresholdDb + 72.2) / 72.2 * 100.0, 0.0, 100.0) : 0.0;
    public double GateCutoffMeterPercent => GateEnabled ? DbToObsMeterPercent(_profile.GateThresholdDb) : 0.0;
    public GridLength GateCutoffActiveWidth => new(Math.Max(0.001, GateCutoffMeterPercent), GridUnitType.Star);
    public GridLength GateCutoffRemainingWidth => new(Math.Max(0.001, 100.0 - GateCutoffMeterPercent), GridUnitType.Star);
    public string GateCutoffLabel => GateEnabled ? $"Gate Cutoff: {_profile.GateThresholdDb} dB" : "Gate: OFF (Open)";
    public Visibility GateZoneVisibility => GateEnabled ? Visibility.Visible : Visibility.Collapsed;

    public double CompressorCatchMeterPercent => CompressorEnabled ? DbToObsMeterPercent(_profile.CompressorThresholdDb) : 100.0;
    public GridLength CompressorPreWidth => new(Math.Max(0.001, CompressorCatchMeterPercent), GridUnitType.Star);
    public GridLength CompressorActiveWidth => new(Math.Max(0.001, 100.0 - CompressorCatchMeterPercent), GridUnitType.Star);
    public string CompressorMeterLabel => CompressorEnabled
        ? $"Leveler: > {_profile.CompressorThresholdDb} dB (+{_profile.CompressorMakeUpGainDb} dB)"
        : "Leveler: OFF";
    public Visibility CompressorZoneVisibility => CompressorEnabled ? Visibility.Visible : Visibility.Collapsed;

    public string EqMeterSummaryLabel => !EqEnabled
        ? "Tone: Flat (0 dB)"
        : $"Tone: {EqSimpleSummaryText} (Low {(_profile.Eq90HzGain >= 0 ? "+" : "")}{_profile.Eq90HzGain}dB, Air {(_profile.Eq3KHzGain >= 0 ? "+" : "")}{_profile.Eq3KHzGain}dB)";

    public int GateAttenuationPercent
    {
        get => _profile.GateAttenuationPercent;
        set
        {
            int c = Math.Clamp(value, 0, 100);
            if (_profile.GateAttenuationPercent != c)
            {
                _profile.GateAttenuationPercent = c;
                OnPropertyChanged();
                ApplyNoiseGateToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public int GateAttackIndex
    {
        get => _profile.GateAttackIndex;
        set
        {
            int c = Math.Clamp(value, 0, DspTables.GateTimesMs.Length - 1);
            if (_profile.GateAttackIndex != c)
            {
                _profile.GateAttackIndex = c;
                OnPropertyChanged();
                ApplyNoiseGateToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public int GateReleaseIndex
    {
        get => _profile.GateReleaseIndex;
        set
        {
            int c = Math.Clamp(value, 0, DspTables.GateTimesMs.Length - 1);
            if (_profile.GateReleaseIndex != c)
            {
                _profile.GateReleaseIndex = c;
                OnPropertyChanged();
                ApplyNoiseGateToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    // Compressor
    public bool CompressorEnabled
    {
        get => _profile.CompressorEnabled;
        set
        {
            if (_profile.CompressorEnabled != value)
            {
                _profile.CompressorEnabled = value;
                OnPropertyChanged();
                NotifyVoiceShaperProperties();
                ApplyCompressorToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public bool CompressorSimpleMode
    {
        get => !_profile.CompressorAdvancedMode;
        set { if (value) CompressorAdvancedMode = false; }
    }

    public bool CompressorAdvancedMode
    {
        get => _profile.CompressorAdvancedMode;
        set
        {
            if (_profile.CompressorAdvancedMode != value)
            {
                _profile.CompressorAdvancedMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CompressorSimpleMode));
                OnPropertyChanged(nameof(CompressorSimpleVisibility));
                OnPropertyChanged(nameof(CompressorAdvancedVisibility));
                AutoSaveIfEnabled();
            }
        }
    }

    public Visibility CompressorSimpleVisibility => _profile.CompressorAdvancedMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CompressorAdvancedVisibility => _profile.CompressorAdvancedMode ? Visibility.Visible : Visibility.Collapsed;

    public int CompressorSimpleAmount
    {
        get => _profile.CompressorSimpleAmount;
        set
        {
            int c = Math.Clamp(value, 0, 100);
            if (_profile.CompressorSimpleAmount != c)
            {
                _profile.CompressorSimpleAmount = c;
                _profile.CompressorThresholdDb = (int)Math.Round(-40.0 * (c / 100.0));
                _profile.CompressorRatioIndex = c > 0 ? 8 : 0; // 3.2:1 or 1:1
                _profile.CompressorMakeUpGainDb = (int)Math.Round((c / 100.0) * 10.0);
                OnPropertyChanged();
                OnPropertyChanged(nameof(CompressorThresholdDb));
                OnPropertyChanged(nameof(CompressorRatioIndex));
                OnPropertyChanged(nameof(CompressorMakeUpGainDb));
                NotifyVoiceShaperProperties();
                ApplyCompressorToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public int CompressorThresholdDb
    {
        get => _profile.CompressorThresholdDb;
        set
        {
            int c = Math.Clamp(value, -40, 0);
            if (_profile.CompressorThresholdDb != c)
            {
                _profile.CompressorThresholdDb = c;
                OnPropertyChanged();
                NotifyVoiceShaperProperties();
                ApplyCompressorToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public int CompressorRatioIndex
    {
        get => _profile.CompressorRatioIndex;
        set
        {
            int c = Math.Clamp(value, 0, DspTables.CompressorRatios.Length - 1);
            if (_profile.CompressorRatioIndex != c)
            {
                _profile.CompressorRatioIndex = c;
                OnPropertyChanged();
                ApplyCompressorToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public int CompressorAttackIndex
    {
        get => _profile.CompressorAttackIndex;
        set
        {
            int c = Math.Clamp(value, 0, DspTables.CompressorAttackLabels.Length - 1);
            if (_profile.CompressorAttackIndex != c)
            {
                _profile.CompressorAttackIndex = c;
                OnPropertyChanged();
                ApplyCompressorToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public int CompressorReleaseIndex
    {
        get => _profile.CompressorReleaseIndex;
        set
        {
            int c = Math.Clamp(value, 0, DspTables.CompressorReleaseLabels.Length - 1);
            if (_profile.CompressorReleaseIndex != c)
            {
                _profile.CompressorReleaseIndex = c;
                OnPropertyChanged();
                ApplyCompressorToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public int CompressorMakeUpGainDb
    {
        get => _profile.CompressorMakeUpGainDb;
        set
        {
            int c = Math.Clamp(value, 0, 24);
            if (_profile.CompressorMakeUpGainDb != c)
            {
                _profile.CompressorMakeUpGainDb = c;
                OnPropertyChanged();
                NotifyVoiceShaperProperties();
                ApplyCompressorToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    // De-Esser
    public bool DeEsserEnabled
    {
        get => _profile.DeEsserEnabled;
        set
        {
            if (_profile.DeEsserEnabled != value)
            {
                _profile.DeEsserEnabled = value;
                OnPropertyChanged();
                _device.SetEffectParams([(EffectKey.DeEsser, _profile.DeEsserEnabled ? _profile.DeEsserPercent : 0)]);
                AutoSaveIfEnabled();
            }
        }
    }

    public int DeEsserPercent
    {
        get => _profile.DeEsserPercent;
        set
        {
            int c = Math.Clamp(value, 0, 100);
            if (_profile.DeEsserPercent != c)
            {
                _profile.DeEsserPercent = c;
                OnPropertyChanged();
                _device.SetEffectParams([(EffectKey.DeEsser, _profile.DeEsserEnabled ? c : 0)]);
                AutoSaveIfEnabled();
            }
        }
    }

    // Equalizer (Simple 3-Band + Advanced 6-Band)
    public bool EqEnabled
    {
        get => _profile.EqEnabled;
        set
        {
            if (_profile.EqEnabled != value)
            {
                _profile.EqEnabled = value;
                OnPropertyChanged();
                ApplyEqToHardware();
                AutoSaveIfEnabled();
            }
        }
    }

    public bool EqSimpleMode
    {
        get => !_profile.EqAdvancedMode;
        set { if (value) EqAdvancedMode = false; }
    }

    public bool EqAdvancedMode
    {
        get => _profile.EqAdvancedMode;
        set
        {
            if (_profile.EqAdvancedMode != value)
            {
                _profile.EqAdvancedMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EqSimpleMode));
                OnPropertyChanged(nameof(EqSimpleVisibility));
                OnPropertyChanged(nameof(EqAdvancedVisibility));
                AutoSaveIfEnabled();
            }
        }
    }

    public Visibility EqSimpleVisibility => _profile.EqAdvancedMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EqAdvancedVisibility => _profile.EqAdvancedMode ? Visibility.Visible : Visibility.Collapsed;

    public int EqSimpleBassGain
    {
        get => (_profile.Eq90HzGain + _profile.Eq250HzGain) / 2;
        set
        {
            int c = Math.Clamp(value, -9, 9);
            _profile.Eq90HzGain = c;
            _profile.Eq250HzGain = c;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Eq90HzGain));
            OnPropertyChanged(nameof(Eq250HzGain));
            ApplyEqToHardware();
            AutoSaveIfEnabled();
        }
    }

    public int EqSimpleMidGain
    {
        get => (_profile.Eq500HzGain + _profile.Eq1KHzGain) / 2;
        set
        {
            int c = Math.Clamp(value, -9, 9);
            _profile.Eq500HzGain = c;
            _profile.Eq1KHzGain = c;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Eq500HzGain));
            OnPropertyChanged(nameof(Eq1KHzGain));
            ApplyEqToHardware();
            AutoSaveIfEnabled();
        }
    }

    public int EqSimpleTrebleGain
    {
        get => (_profile.Eq3KHzGain + _profile.Eq8KHzGain) / 2;
        set
        {
            int c = Math.Clamp(value, -9, 9);
            _profile.Eq3KHzGain = c;
            _profile.Eq8KHzGain = c;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Eq3KHzGain));
            OnPropertyChanged(nameof(Eq8KHzGain));
            ApplyEqToHardware();
            AutoSaveIfEnabled();
        }
    }

    public int Eq90HzGain
    {
        get => _profile.Eq90HzGain;
        set { if (_profile.Eq90HzGain != Math.Clamp(value, -9, 9)) { _profile.Eq90HzGain = Math.Clamp(value, -9, 9); OnPropertyChanged(); OnPropertyChanged(nameof(EqSimpleBassGain)); NotifyVoiceShaperProperties(); ApplyEqToHardware(); AutoSaveIfEnabled(); } }
    }

    public int Eq250HzGain
    {
        get => _profile.Eq250HzGain;
        set { if (_profile.Eq250HzGain != Math.Clamp(value, -9, 9)) { _profile.Eq250HzGain = Math.Clamp(value, -9, 9); OnPropertyChanged(); OnPropertyChanged(nameof(EqSimpleBassGain)); NotifyVoiceShaperProperties(); ApplyEqToHardware(); AutoSaveIfEnabled(); } }
    }

    public int Eq500HzGain
    {
        get => _profile.Eq500HzGain;
        set { if (_profile.Eq500HzGain != Math.Clamp(value, -9, 9)) { _profile.Eq500HzGain = Math.Clamp(value, -9, 9); OnPropertyChanged(); OnPropertyChanged(nameof(EqSimpleMidGain)); NotifyVoiceShaperProperties(); ApplyEqToHardware(); AutoSaveIfEnabled(); } }
    }

    public int Eq1KHzGain
    {
        get => _profile.Eq1KHzGain;
        set { if (_profile.Eq1KHzGain != Math.Clamp(value, -9, 9)) { _profile.Eq1KHzGain = Math.Clamp(value, -9, 9); OnPropertyChanged(); OnPropertyChanged(nameof(EqSimpleMidGain)); NotifyVoiceShaperProperties(); ApplyEqToHardware(); AutoSaveIfEnabled(); } }
    }

    public int Eq3KHzGain
    {
        get => _profile.Eq3KHzGain;
        set { if (_profile.Eq3KHzGain != Math.Clamp(value, -9, 9)) { _profile.Eq3KHzGain = Math.Clamp(value, -9, 9); OnPropertyChanged(); OnPropertyChanged(nameof(EqSimpleTrebleGain)); NotifyVoiceShaperProperties(); ApplyEqToHardware(); AutoSaveIfEnabled(); } }
    }

    public int Eq8KHzGain
    {
        get => _profile.Eq8KHzGain;
        set { if (_profile.Eq8KHzGain != Math.Clamp(value, -9, 9)) { _profile.Eq8KHzGain = Math.Clamp(value, -9, 9); OnPropertyChanged(); OnPropertyChanged(nameof(EqSimpleTrebleGain)); NotifyVoiceShaperProperties(); ApplyEqToHardware(); AutoSaveIfEnabled(); } }
    }

    // --- Actions & Hardware Sync ---

    public void TryConnectDevice()
    {
        if (_device.Connect(out string err))
        {
            IsConnected = true;
            ConnectionStatusText = $"{_device.DeviceModel} • FW {_device.FirmwareVersion} • SN {_device.SerialNumber}";
            StatusBannerMessage = "Connected to GoXLR Mini hardware.";
            OnPropertyChanged(nameof(DeviceModelText));
            OnPropertyChanged(nameof(DeviceFirmwareText));
            OnPropertyChanged(nameof(DeviceSerialText));
            ApplyFullProfileToHardware(applyStartupLevelRule: true);
        }
        else
        {
            IsConnected = false;
            ConnectionStatusText = "Disconnected (Click Reconnect)";
            StatusBannerMessage = err;
            OnPropertyChanged(nameof(DeviceModelText));
            OnPropertyChanged(nameof(DeviceFirmwareText));
            OnPropertyChanged(nameof(DeviceSerialText));
        }
    }

    public async Task ReconnectDeviceAsync(CancellationToken ct = default)
    {
        if (_isDisposed) return;
        if (!await _connectSemaphore.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return; // Connect attempt already in progress
        }

        try
        {
            bool connected = false;
            string err = string.Empty;
            await Task.Run(() => connected = _device.Connect(out err), ct).ConfigureAwait(false);
            if (!connected)
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    if (_isDisposed) return;
                    IsConnected = false;
                    ConnectionStatusText = "Disconnected (Click Reconnect)";
                    StatusBannerMessage = err;
                    OnPropertyChanged(nameof(DeviceModelText));
                    OnPropertyChanged(nameof(DeviceFirmwareText));
                    OnPropertyChanged(nameof(DeviceSerialText));
                }, DispatcherPriority.DataBind, ct);
                return;
            }

            // Snapshot current fader assignments on UI thread, then configure faders + read live hardware state off-thread
            var faderMap = await _uiDispatcher.InvokeAsync(
                () => new Dictionary<FaderName, ChannelName>(_profile.FaderAssignments),
                DispatcherPriority.DataBind,
                ct);

            HardwareButtonAndFaderState? hwState = await Task.Run(async () =>
            {
                foreach (var fader in Enum.GetValues<FaderName>())
                {
                    var ch = faderMap.GetValueOrDefault(fader, ChannelName.Mic);
                    _device.SetFaderChannel(fader, ch);
                    _device.SetFaderDisplayMode(fader, gradient: false, meter: false);
                }

                HardwareButtonAndFaderState? readState = null;
                for (int attempt = 0; attempt < 8 && _device.IsConnected; attempt++)
                {
                    await Task.Delay(attempt == 0 ? 25 : 50, ct).ConfigureAwait(false);
                    readState = _device.GetButtonStates();
                    if (readState != null) break;
                }
                return readState;
            }, ct).ConfigureAwait(false);

            // Reconcile state into _profile on UI thread and build immutable push action
            Action? backgroundPush = await _uiDispatcher.InvokeAsync(() =>
            {
                if (_isDisposed || !_device.IsConnected) return (Action?)null;

                IsConnected = true;
                ConnectionStatusText = $"{_device.DeviceModel} • FW {_device.FirmwareVersion} • SN {_device.SerialNumber}";
                StatusBannerMessage = "Connected to GoXLR Mini hardware.";
                OnPropertyChanged(nameof(DeviceModelText));
                OnPropertyChanged(nameof(DeviceFirmwareText));
                OnPropertyChanged(nameof(DeviceSerialText));

                ReconcileProfileForHardwareSync(hwState, applyStartupLevelRule: true);
                var pushAction = CaptureFullHardwarePushAction();
                SyncViewModelsFromProfile();
                UpdateAnimationCache();
                return pushAction;
            }, DispatcherPriority.DataBind, ct);

            if (backgroundPush != null)
            {
                await Task.Run(backgroundPush, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            RunOnUi(() => StatusBannerMessage = $"Hardware sync error: {ex.Message}");
        }
        finally
        {
            _connectSemaphore.Release();
        }
    }

    private void ReconcileProfileForHardwareSync(HardwareButtonAndFaderState? hwState, bool applyStartupLevelRule)
    {
        // The GoXLR Mini reports 0 for a fader until that fader physically moves (confirmed from raw USB logs),
        // so a 0 reading (or a failed read) means "position unknown", NOT "fader at 0". Decide per fader, because
        // after some faders have moved the others still report 0.
        _pendingFaderSync = false;
        _isBleepHeld = false;
        _coughMutedOnPress = false;
        if (hwState != null) _lastButtonsMask = hwState.PressedButtonsMask;

        foreach (var fader in Enum.GetValues<FaderName>())
        {
            byte physVol = hwState?.GetFaderVolume(fader) ?? 0;
            var assignedCh = _profile.FaderAssignments.GetValueOrDefault(fader, ChannelName.Mic);
            if (physVol == 0)
            {
                // Unknown: start at unity (100%) (or saved level if configured and non-zero) and ignore 0 readings until this fader moves.
                byte fallbackVol = (_settings.StartupBehavior == StartupLevelBehavior.UseProfileSavedLevels)
                    ? _profile.ChannelVolumes.GetValueOrDefault(assignedCh, (byte)255)
                    : (byte)255;
                if (fallbackVol == 0) fallbackVol = 255;
                _profile.ChannelVolumes[assignedCh] = fallbackVol;
                _faderLastSeen[(int)fader] = 0;
                _faderUnreported[(int)fader] = true;
            }
            else
            {
                // Physical potentiometer position on the desk always sets the software volume on connect.
                _profile.ChannelVolumes[assignedCh] = physVol;
                _faderLastSeen[(int)fader] = physVol;
                _faderUnreported[(int)fader] = false;
            }
        }

        if (applyStartupLevelRule)
        {
            // Ensure Microphone, System (PC desktop sound), Headphones, BroadcastMix, and ChatMic are active
            _profile.DisabledInputs.Remove(BasicInputDevice.Microphone);
            _profile.DisabledInputs.Remove(BasicInputDevice.System);
            _profile.DisabledOutputs.Remove(BasicOutputDevice.BroadcastMix);
            _profile.DisabledOutputs.Remove(BasicOutputDevice.Headphones);
            _profile.DisabledOutputs.Remove(BasicOutputDevice.ChatMic);

            if (_settings.StartupBehavior != StartupLevelBehavior.UseProfileSavedLevels)
            {
                // Ensure core daily playback channels never boot unexpectedly muted from a stale state
                _profile.ChannelMuted[ChannelName.System] = false;
                _profile.ChannelMuted[ChannelName.Chat] = false;
                _profile.ChannelMuted[ChannelName.Game] = false;
                _profile.ChannelMuted[ChannelName.Headphones] = false;
            }

            if (_settings.StartupBehavior == StartupLevelBehavior.Force100Percent)
            {
                foreach (var ch in Enum.GetValues<ChannelName>())
                {
                    _profile.ChannelVolumes[ch] = 255;
                }
            }
            else if (_settings.StartupBehavior == StartupLevelBehavior.SyncPhysicalFaders)
            {
                foreach (var ch in Enum.GetValues<ChannelName>())
                {
                    if (!_profile.FaderAssignments.Values.Contains(ch) && ch != ChannelName.MicMonitor)
                    {
                        _profile.ChannelVolumes[ch] = 255;
                    }
                }
            }
        }

        if (!_profile.Routing.ContainsKey(BasicInputDevice.Microphone))
        {
            _profile.Routing[BasicInputDevice.Microphone] = new Dictionary<BasicOutputDevice, bool>();
        }
        _profile.Routing[BasicInputDevice.Microphone][BasicOutputDevice.Headphones] = SidetoneEnabled;
    }

    private Action CaptureFullHardwarePushAction()
    {
        var chSnapshots = Enum.GetValues<ChannelName>().Select(ch =>
        {
            bool isDisabled = IsChannelCurrentlyDisabled(ch);
            bool muted = _profile.ChannelMuted.GetValueOrDefault(ch, false) || isDisabled;
            byte vol = muted ? (byte)0 : _profile.ChannelVolumes.GetValueOrDefault(ch, (byte)255);
            return (Channel: ch, Vol: vol, Muted: muted, Visible: !isDisabled);
        }).ToArray();

        var outSnapshots = Enum.GetValues<BasicOutputDevice>()
            .Select(outp => (Output: outp, Enabled: !_profile.DisabledOutputs.Contains(outp)))
            .ToArray();

        var routingSnapshots = Enum.GetValues<BasicInputDevice>()
            .Select(inp => (Input: inp, Effective: ComputeEffectiveRoutingForInput(inp)))
            .ToArray();

        var micType = _profile.MicType;
        ushort micGainDb = _profile.GetActiveMicGainDb();

        bool gateOn = _profile.GateEnabled;
        int gateThresh = gateOn ? _profile.GateThresholdDb : -59;
        int gateAttack = _profile.GateAttackIndex;
        int gateRelease = _profile.GateReleaseIndex;
        sbyte gateAtten = gateOn ? DspTables.GateAttenuationFromPercent(_profile.GateAttenuationPercent) : (sbyte)0;

        bool compOn = _profile.CompressorEnabled;
        int compThresh = compOn ? _profile.CompressorThresholdDb : 0;
        int compRatioIdx = compOn ? _profile.CompressorRatioIndex : 0;
        float compRatioFloat = DspTables.CompressorRatios[Math.Clamp(compRatioIdx, 0, DspTables.CompressorRatios.Length - 1)];
        int compAttack = _profile.CompressorAttackIndex;
        int compRelease = _profile.CompressorReleaseIndex;
        int compMakeUp = compOn ? EffectiveCompressorMakeUpDb : 0;

        bool eqOn = _profile.EqEnabled;
        var eqParams = new (MicrophoneParamKey Key, float Val)[]
        {
            (MicrophoneParamKey.Equalizer90HzFrequency, _profile.Eq90HzFreq),
            (MicrophoneParamKey.Equalizer90HzGain, eqOn ? _profile.Eq90HzGain : 0),
            (MicrophoneParamKey.Equalizer250HzFrequency, _profile.Eq250HzFreq),
            (MicrophoneParamKey.Equalizer250HzGain, eqOn ? _profile.Eq250HzGain : 0),
            (MicrophoneParamKey.Equalizer500HzFrequency, _profile.Eq500HzFreq),
            (MicrophoneParamKey.Equalizer500HzGain, eqOn ? _profile.Eq500HzGain : 0),
            (MicrophoneParamKey.Equalizer1KHzFrequency, _profile.Eq1KHzFreq),
            (MicrophoneParamKey.Equalizer1KHzGain, eqOn ? _profile.Eq1KHzGain : 0),
            (MicrophoneParamKey.Equalizer3KHzFrequency, _profile.Eq3KHzFreq),
            (MicrophoneParamKey.Equalizer3KHzGain, eqOn ? _profile.Eq3KHzGain : 0),
            (MicrophoneParamKey.Equalizer8KHzFrequency, _profile.Eq8KHzFreq),
            (MicrophoneParamKey.Equalizer8KHzGain, eqOn ? _profile.Eq8KHzGain : 0)
        };

        int bleepLevelDb = _profile.BleepLevelDb;
        int deEsserVal = _profile.DeEsserEnabled ? _profile.DeEsserPercent : 0;
        bool micMuted = _profile.ChannelMuted.GetValueOrDefault(ChannelName.Mic, false) || IsChannelCurrentlyDisabled(ChannelName.Mic);

        byte[] buttonLedStates = BuildButtonLedStates();
        bool[] faderMuted = new bool[4];
        FaderName[] faders = [FaderName.A, FaderName.B, FaderName.C, FaderName.D];
        for (int i = 0; i < 4; i++)
        {
            var ch = _profile.FaderAssignments.GetValueOrDefault(faders[i], ChannelName.Mic);
            faderMuted[i] = _profile.ChannelMuted.GetValueOrDefault(ch, false) || IsChannelCurrentlyDisabled(ch);
        }
        var zA = ParseHexRgb(_profile.FaderAColor);
        var zB = ParseHexRgb(_profile.FaderBColor);
        var zC = ParseHexRgb(_profile.FaderCColor);
        var zD = ParseHexRgb(_profile.FaderDColor);
        string effectName = _profile.LightingEffect;

        return () =>
        {
            if (!_device.IsConnected) return;

            foreach (var c in chSnapshots)
            {
                _device.SetChannelVolume(c.Channel, c.Vol);
                _device.SetChannelMuted(c.Channel, c.Muted);
                WindowsAudioEndpointManager.SetPlaybackChannelVisibleInWindows(c.Channel, c.Visible);
            }

            foreach (var o in outSnapshots)
            {
                WindowsAudioEndpointManager.SetRecordingOutputVisibleInWindows(o.Output, o.Enabled);
            }

            WindowsAudioEndpointManager.SetGoXlrChatMicAsDefaultRecording();

            foreach (var r in routingSnapshots)
            {
                _device.SetRoutingForInput(r.Input, r.Effective);
            }

            _device.SetMicTypeAndGain(micType, micGainDb);

            _device.SetMicrophoneFloatParams([
                (MicrophoneParamKey.GateThreshold, gateThresh),
                (MicrophoneParamKey.GateAttack, gateAttack),
                (MicrophoneParamKey.GateRelease, gateRelease),
                (MicrophoneParamKey.GateAttenuation, gateAtten)
            ]);
            _device.SetEffectParams([
                (EffectKey.GateMode, 2),
                (EffectKey.GateEnabled, gateOn ? 1 : 0),
                (EffectKey.GateThreshold, gateThresh),
                (EffectKey.GateAttack, gateAttack),
                (EffectKey.GateRelease, gateRelease),
                (EffectKey.GateAttenuation, gateAtten)
            ]);

            _device.SetMicrophoneFloatParams([
                (MicrophoneParamKey.CompressorThreshold, compThresh),
                (MicrophoneParamKey.CompressorRatio, compRatioFloat),
                (MicrophoneParamKey.CompressorAttack, compAttack),
                (MicrophoneParamKey.CompressorRelease, compRelease),
                (MicrophoneParamKey.CompressorMakeUpGain, compMakeUp)
            ]);
            _device.SetEffectParams([
                (EffectKey.CompressorThreshold, compThresh),
                (EffectKey.CompressorRatio, compRatioIdx),
                (EffectKey.CompressorAttack, compAttack),
                (EffectKey.CompressorRelease, compRelease),
                (EffectKey.CompressorMakeUpGain, compMakeUp)
            ]);

            _device.SetMicrophoneFloatParams(eqParams);
            _device.SetBleepLevel(bleepLevelDb);
            _device.SetEffectParams([
                (EffectKey.DeEsser, deEsserVal),
                (EffectKey.MicInputMute, micMuted ? 1 : 0)
            ]);

            _device.SetButtonStates(buttonLedStates);
            PushLightingToDevice(effectName, zA, zB, zC, zD, faderMuted);
        };
    }

    public void ApplyFullProfileToHardware(bool applyStartupLevelRule)
    {
        if (!_device.IsConnected) return;

        try
        {
            foreach (var fader in Enum.GetValues<FaderName>())
            {
                var ch = _profile.FaderAssignments.GetValueOrDefault(fader, ChannelName.Mic);
                _device.SetFaderChannel(fader, ch);
                _device.SetFaderDisplayMode(fader, gradient: false, meter: false);
            }

            HardwareButtonAndFaderState? hwState = null;
            for (int attempt = 0; attempt < 8 && _device.IsConnected; attempt++)
            {
                Thread.Sleep(attempt == 0 ? 25 : 50);
                hwState = _device.GetButtonStates();
                if (hwState != null) break;
            }

            ReconcileProfileForHardwareSync(hwState, applyStartupLevelRule);
            var pushAction = CaptureFullHardwarePushAction();
            pushAction();
            SyncViewModelsFromProfile();
            UpdateAnimationCache();
        }
        catch (Exception ex)
        {
            StatusBannerMessage = $"Hardware sync error: {ex.Message}";
        }
    }

    public void SetAllLevelsTo100Percent()
    {
        foreach (var ch in Enum.GetValues<ChannelName>())
        {
            _profile.ChannelVolumes[ch] = 255;
            if (!IsChannelCurrentlyDisabled(ch) && !_profile.ChannelMuted.GetValueOrDefault(ch, false))
            {
                _device.SetChannelVolume(ch, 255);
            }
        }
        SyncViewModelsFromProfile();
        AutoSaveIfEnabled();
        StatusBannerMessage = "All active channels set to 100% volume.";
    }

    public bool IsMusicMutedOnStream
    {
        get => _profile.Routing.TryGetValue(BasicInputDevice.Music, out var m) && !m.GetValueOrDefault(BasicOutputDevice.BroadcastMix, true);
        set
        {
            if (!_profile.Routing.ContainsKey(BasicInputDevice.Music))
                _profile.Routing[BasicInputDevice.Music] = new Dictionary<BasicOutputDevice, bool>();
            _profile.Routing[BasicInputDevice.Music][BasicOutputDevice.Headphones] = true;
            _profile.Routing[BasicInputDevice.Music][BasicOutputDevice.BroadcastMix] = !value;
            PushRoutingForInput(BasicInputDevice.Music);
            SyncViewModelsFromProfile();
            AutoSaveIfEnabled();
            StatusBannerMessage = value
                ? "Music muted on Stream Mix (You still hear music in your headphones)."
                : "Music reconnected to Stream Mix.";
        }
    }

    public bool IsChatPrivateInHeadset
    {
        get => _profile.Routing.TryGetValue(BasicInputDevice.Chat, out var c) && !c.GetValueOrDefault(BasicOutputDevice.BroadcastMix, true);
        set
        {
            if (!_profile.Routing.ContainsKey(BasicInputDevice.Chat))
                _profile.Routing[BasicInputDevice.Chat] = new Dictionary<BasicOutputDevice, bool>();
            _profile.Routing[BasicInputDevice.Chat][BasicOutputDevice.Headphones] = true;
            _profile.Routing[BasicInputDevice.Chat][BasicOutputDevice.BroadcastMix] = !value;
            PushRoutingForInput(BasicInputDevice.Chat);
            SyncViewModelsFromProfile();
            AutoSaveIfEnabled();
            StatusBannerMessage = value
                ? "Voice Chat kept private in Headphones (Hidden from Stream Mix)."
                : "Voice Chat reconnected to Stream Mix.";
        }
    }

    public string HeadphonesConnectedSummary => GetConnectedInputsForOutputSummary(BasicOutputDevice.Headphones);
    public string StreamMixConnectedSummary => GetConnectedInputsForOutputSummary(BasicOutputDevice.BroadcastMix);
    public string ChatMicConnectedSummary => GetConnectedInputsForOutputSummary(BasicOutputDevice.ChatMic);
    public string LineOutConnectedSummary => GetConnectedInputsForOutputSummary(BasicOutputDevice.LineOut);

    private string GetConnectedInputsForOutputSummary(BasicOutputDevice output)
    {
        var list = new List<string>();
        foreach (var inp in Enum.GetValues<BasicInputDevice>())
        {
            if (_profile.DisabledInputs.Contains(inp)) continue;
            if (output == BasicOutputDevice.ChatMic && inp == BasicInputDevice.Chat) continue;
            if (_profile.Routing.TryGetValue(inp, out var map) && map.GetValueOrDefault(output, false))
            {
                list.Add(DspTables.GetInputDisplayName(inp));
            }
        }
        return list.Count > 0 ? $"Receiving: {string.Join(", ", list)}" : "No audio sources connected";
    }

    public void ApplyFaderLayoutPreset(string presetKey)
    {
        switch (presetKey)
        {
            case "GamingMaster":
                _profile.FaderAssignments[FaderName.A] = ChannelName.Mic;
                _profile.FaderAssignments[FaderName.B] = ChannelName.Game;
                _profile.FaderAssignments[FaderName.C] = ChannelName.Chat;
                _profile.FaderAssignments[FaderName.D] = ChannelName.Headphones;
                StatusBannerMessage = "Applied Slider Layout: 1=Mic, 2=Game, 3=Chat (Discord), 4=Headphones Master.";
                break;

            case "DiscordStreamer":
            default:
                _profile.FaderAssignments[FaderName.A] = ChannelName.Mic;
                _profile.FaderAssignments[FaderName.B] = ChannelName.System;
                _profile.FaderAssignments[FaderName.C] = ChannelName.Chat;
                _profile.FaderAssignments[FaderName.D] = ChannelName.Headphones;
                StatusBannerMessage = "Applied Slider Layout: 1=Mic, 2=System/Desktop, 3=Chat (Discord), 4=Headphones Master.";
                break;
        }

        foreach (var fader in Enum.GetValues<FaderName>())
        {
            var ch = _profile.FaderAssignments[fader];
            _device.SetFaderChannel(fader, ch);
            _device.SetFaderDisplayMode(fader, gradient: false, meter: false);
            WindowsAudioEndpointManager.SetPlaybackChannelVisibleInWindows(ch, true);
        }

        foreach (var inp in Enum.GetValues<BasicInputDevice>())
        {
            PushRoutingForInput(inp);
        }

        WindowsAudioEndpointManager.SyncAllGoXlrEndpoints(_profile);
        SyncViewModelsFromProfile();
        NotifyChannelEnabledProperties();
        AutoSaveIfEnabled();
    }

    // --- GUIDED 1-CLICK VOICE SHAPER METHODS (NO AUDIO ENGINEERING DEGREE REQUIRED) ---
    public void SetGatePreset(string mode)
    {
        switch (mode)
        {
            case "Off":
                _profile.GateEnabled = false;
                StatusBannerMessage = "Step 1 (Background Silence): Off — microphone stays open at all times.";
                break;
            case "Light":
                _profile.GateEnabled = true;
                _profile.GateThresholdDb = -52;
                _profile.GateAttenuationPercent = 75;
                _profile.GateReleaseIndex = 22;
                StatusBannerMessage = "Step 1 (Background Silence): Light (-52 dB) — blocks quiet computer fan hum.";
                break;
            case "Strict":
                _profile.GateEnabled = true;
                _profile.GateThresholdDb = -34;
                _profile.GateAttenuationPercent = 100;
                _profile.GateReleaseIndex = 10;
                StatusBannerMessage = "Step 1 (Background Silence): Strict (-34 dB) — cuts mechanical keyboard typing & clicks.";
                break;
            case "Max":
                _profile.GateEnabled = true;
                _profile.GateThresholdDb = -26;
                _profile.GateAttenuationPercent = 100;
                _profile.GateReleaseIndex = 8;
                StatusBannerMessage = "Step 1 (Background Silence): Max (-26 dB) — only opens when speaking close to the mic.";
                break;
            default: // Balanced
                _profile.GateEnabled = true;
                _profile.GateThresholdDb = -42;
                _profile.GateAttenuationPercent = 100;
                _profile.GateReleaseIndex = 18;
                StatusBannerMessage = "Step 1 (Background Silence): Balanced (-42 dB) — ideal room noise blocker.";
                break;
        }
        ApplyNoiseGateToHardware();
        SyncViewModelsFromProfile();
        AutoSaveIfEnabled();
    }

    public void SetEqPreset(string mode)
    {
        switch (mode)
        {
            case "Flat":
                _profile.EqEnabled = false;
                _profile.Eq90HzGain = 0;
                _profile.Eq250HzGain = 0;
                _profile.Eq500HzGain = 0;
                _profile.Eq1KHzGain = 0;
                _profile.Eq3KHzGain = 0;
                _profile.Eq8KHzGain = 0;
                StatusBannerMessage = "Step 2 (Voice Tone): Natural Flat — uncolored microphone sound.";
                break;
            case "Crisp":
                _profile.EqEnabled = true;
                _profile.Eq90HzGain = -1;
                _profile.Eq250HzGain = 0;
                _profile.Eq500HzGain = 0;
                _profile.Eq1KHzGain = 2;
                _profile.Eq3KHzGain = 4;
                _profile.Eq8KHzGain = 4;
                StatusBannerMessage = "Step 2 (Voice Tone): Crisp & Clear — boosts vocal presence so you cut through game audio.";
                break;
            case "Warm":
                _profile.EqEnabled = true;
                _profile.Eq90HzGain = 4;
                _profile.Eq250HzGain = 3;
                _profile.Eq500HzGain = -1;
                _profile.Eq1KHzGain = 0;
                _profile.Eq3KHzGain = 2;
                _profile.Eq8KHzGain = 2;
                StatusBannerMessage = "Step 2 (Voice Tone): Warm Radio — rich podcast low-end with smooth clarity.";
                break;
            case "Deep":
                _profile.EqEnabled = true;
                _profile.Eq90HzGain = 6;
                _profile.Eq250HzGain = 4;
                _profile.Eq500HzGain = -2;
                _profile.Eq1KHzGain = 0;
                _profile.Eq3KHzGain = 1;
                _profile.Eq8KHzGain = 1;
                StatusBannerMessage = "Step 2 (Voice Tone): Deep Bass — maximum broadcast low-end depth.";
                break;
            default: // Balanced
                _profile.EqEnabled = true;
                _profile.Eq90HzGain = 2;
                _profile.Eq250HzGain = 1;
                _profile.Eq500HzGain = -1;
                _profile.Eq1KHzGain = 1;
                _profile.Eq3KHzGain = 2;
                _profile.Eq8KHzGain = 3;
                StatusBannerMessage = "Step 2 (Voice Tone): Balanced Studio — natural warmth and clear speech.";
                break;
        }
        ApplyEqToHardware();
        SyncViewModelsFromProfile();
        AutoSaveIfEnabled();
    }

    public void SetCompressorPreset(string mode)
    {
        switch (mode)
        {
            case "Off":
                _profile.CompressorEnabled = false;
                StatusBannerMessage = "Step 3 (Auto Volume Leveler): Off — no automatic volume leveling.";
                break;
            case "Gentle":
                _profile.CompressorEnabled = true;
                _profile.CompressorThresholdDb = -14;
                _profile.CompressorRatioIndex = 5; // 1.8:1
                _profile.CompressorMakeUpGainDb = 2;
                StatusBannerMessage = "Step 3 (Auto Volume Leveler): Gentle — lightly catches loud peaks.";
                break;
            case "Broadcast":
                _profile.CompressorEnabled = true;
                _profile.CompressorThresholdDb = -20;
                _profile.CompressorRatioIndex = 9; // 4.0:1
                _profile.CompressorMakeUpGainDb = 6;
                StatusBannerMessage = "Step 3 (Auto Volume Leveler): Broadcast — keeps whispers and shouts at a steady volume.";
                break;
            case "MaxBoost":
                _profile.CompressorEnabled = true;
                _profile.CompressorThresholdDb = -24;
                _profile.CompressorRatioIndex = 11; // 8.0:1
                _profile.CompressorMakeUpGainDb = 9;
                StatusBannerMessage = "Step 3 (Auto Volume Leveler): Max Boost — brings quiet speech right to the front.";
                break;
            default: // Balanced
                _profile.CompressorEnabled = true;
                _profile.CompressorThresholdDb = -18;
                _profile.CompressorRatioIndex = 8; // 3.2:1
                _profile.CompressorMakeUpGainDb = 4;
                StatusBannerMessage = "Step 3 (Auto Volume Leveler): Balanced — smooth vocal consistency.";
                break;
        }
        ApplyCompressorToHardware();
        SyncViewModelsFromProfile();
        AutoSaveIfEnabled();
    }

    public void SetDeEsserPreset(int percent)
    {
        _profile.DeEsserEnabled = percent > 0;
        _profile.DeEsserPercent = Math.Clamp(percent, 0, 100);
        _device.SetEffectParams([(EffectKey.DeEsser, _profile.DeEsserEnabled ? _profile.DeEsserPercent : 0)]);
        SyncViewModelsFromProfile();
        AutoSaveIfEnabled();
        StatusBannerMessage = percent == 0
            ? "Step 4 (Harsh 'S' Softener): Off."
            : $"Step 4 (Harsh 'S' Softener): Set to {percent}% smoothness.";
    }

    public void SetBleepPreset(int db)
    {
        BleepLevelDb = db;
        StatusBannerMessage = $"Censor Bleep (#!*@) volume set to {BleepVolumePercent}% ({db} dB).";
    }

    public string GateSimpleSummaryText => !GateEnabled
        ? "Off (All sound passes through)"
        : GateThresholdDb switch
        {
            <= -48 => $"Light ({GateThresholdDb} dB)",
            <= -38 => $"Balanced ({GateThresholdDb} dB)",
            <= -30 => $"Strict ({GateThresholdDb} dB)",
            _ => $"Max ({GateThresholdDb} dB)"
        };

    public string EqSimpleSummaryText => !EqEnabled
        ? "Natural Flat (Off)"
        : (_profile.Eq90HzGain >= 5 ? "Deep Bass" : _profile.Eq90HzGain >= 3 ? "Warm Radio" : _profile.Eq3KHzGain >= 4 ? "Crisp & Clear" : "Balanced Studio");

    public string CompressorSimpleSummaryText => !CompressorEnabled
        ? "Off (No auto-leveling)"
        : CompressorMakeUpGainDb switch
        {
            <= 2 => $"Gentle (+{CompressorMakeUpGainDb} dB boost)",
            <= 4 => $"Balanced (+{CompressorMakeUpGainDb} dB boost)",
            <= 6 => $"Broadcast (+{CompressorMakeUpGainDb} dB boost)",
            _ => $"Max Boost (+{CompressorMakeUpGainDb} dB boost)"
        };

    public string DeEsserSimpleSummaryText => !DeEsserEnabled || DeEsserPercent == 0
        ? "Off"
        : $"{DeEsserPercent}% Smoothness";

    public void ApplyRoutingPreset(string presetKey)
    {
        switch (presetKey)
        {
            case "StreamNoMusic":
                foreach (var inp in Enum.GetValues<BasicInputDevice>())
                {
                    _profile.Routing[inp][BasicOutputDevice.Headphones] = inp != BasicInputDevice.Microphone || SidetoneEnabled;
                    _profile.Routing[inp][BasicOutputDevice.BroadcastMix] = inp != BasicInputDevice.Music;
                }
                StatusBannerMessage = "Applied preset: Hide Music from Broadcast Stream Mix.";
                break;

            case "ChatOnlyInHeadphones":
                _profile.Routing[BasicInputDevice.Chat][BasicOutputDevice.Headphones] = true;
                _profile.Routing[BasicInputDevice.Chat][BasicOutputDevice.BroadcastMix] = false;
                StatusBannerMessage = "Applied preset: Voice Chat only in Headphones (hidden from stream).";
                break;

            case "ResetDefault":
                _profile.Routing = XlrProfile.CreateDefaultRouting();
                _profile.Routing[BasicInputDevice.Microphone][BasicOutputDevice.Headphones] = SidetoneEnabled;
                StatusBannerMessage = "Reset routing matrix to default.";
                break;

            case "StandardBroadcast":
                foreach (var inp in Enum.GetValues<BasicInputDevice>())
                {
                    _profile.Routing[inp][BasicOutputDevice.Headphones] = inp != BasicInputDevice.Microphone || SidetoneEnabled;
                    _profile.Routing[inp][BasicOutputDevice.BroadcastMix] = true;
                }
                StatusBannerMessage = "Applied preset: Standard Broadcast (All audio channels live to stream and headphones).";
                break;

            case "LateNightSolo":
                SetSidetoneState(false);
                _profile.Routing[BasicInputDevice.Microphone][BasicOutputDevice.BroadcastMix] = false;
                _profile.Routing[BasicInputDevice.Microphone][BasicOutputDevice.Headphones] = false;
                _profile.Routing[BasicInputDevice.Game][BasicOutputDevice.Headphones] = true;
                _profile.Routing[BasicInputDevice.Music][BasicOutputDevice.Headphones] = true;
                _profile.Routing[BasicInputDevice.System][BasicOutputDevice.Headphones] = true;
                StatusBannerMessage = "Applied preset: Late Night Solo (Microphone muted; game and music routed to headphones).";
                break;
        }

        foreach (var inp in Enum.GetValues<BasicInputDevice>())
        {
            PushRoutingForInput(inp);
        }
        SyncViewModelsFromProfile();
        AutoSaveIfEnabled();
    }

    public void ApplyMicFilterPreset(string presetName)
    {
        switch (presetName)
        {
            case "BroadcastClean":
                _profile.GateEnabled = true;
                _profile.GateSimpleAmount = 25;
                _profile.GateThresholdDb = -46;
                _profile.GateAttenuationPercent = 100;
                _profile.GateAttackIndex = 0; // 10ms
                _profile.GateReleaseIndex = 19; // 200ms
                _profile.CompressorEnabled = true;
                _profile.CompressorSimpleAmount = 45;
                _profile.CompressorThresholdDb = -18;
                _profile.CompressorRatioIndex = 9; // 4:1
                _profile.CompressorAttackIndex = 1; // 2ms
                _profile.CompressorReleaseIndex = 9; // 100ms
                _profile.CompressorMakeUpGainDb = 6;
                _profile.DeEsserEnabled = true;
                _profile.DeEsserPercent = 25;
                _profile.EqEnabled = true;
                _profile.Eq90HzGain = 3;
                _profile.Eq250HzGain = 2;
                _profile.Eq500HzGain = -1;
                _profile.Eq1KHzGain = 0;
                _profile.Eq3KHzGain = 2;
                _profile.Eq8KHzGain = 3;
                StatusBannerMessage = "Applied Mic Preset: Studio Warm.";
                break;

            case "NoisyRoom":
                _profile.GateEnabled = true;
                _profile.GateSimpleAmount = 43;
                _profile.GateThresholdDb = -36;
                _profile.GateAttenuationPercent = 100;
                _profile.GateAttackIndex = 0;
                _profile.GateReleaseIndex = 14; // 150ms
                _profile.CompressorEnabled = true;
                _profile.CompressorSimpleAmount = 50;
                _profile.CompressorThresholdDb = -20;
                _profile.CompressorRatioIndex = 8; // 3.2:1
                _profile.CompressorMakeUpGainDb = 4;
                _profile.DeEsserEnabled = true;
                _profile.DeEsserPercent = 15;
                _profile.EqEnabled = true;
                _profile.Eq90HzGain = 1;
                _profile.Eq250HzGain = 1;
                _profile.Eq500HzGain = 0;
                _profile.Eq1KHzGain = 1;
                _profile.Eq3KHzGain = 2;
                _profile.Eq8KHzGain = 1;
                StatusBannerMessage = "Applied Mic Preset: Noisy Room Rejection.";
                break;

            case "FlatBypass":
                _profile.GateEnabled = false;
                _profile.CompressorEnabled = false;
                _profile.EqEnabled = false;
                _profile.DeEsserEnabled = false;
                StatusBannerMessage = "All vocal filters bypassed (Natural Flat).";
                break;
        }

        ApplyNoiseGateToHardware();
        ApplyCompressorToHardware();
        ApplyEqToHardware();
        _device.SetEffectParams([(EffectKey.DeEsser, _profile.DeEsserEnabled ? _profile.DeEsserPercent : 0)]);
        SyncViewModelsFromProfile();
        AutoSaveIfEnabled();
    }

    public void SaveCurrentProfile()
    {
        _pendingAutoSave = false;
        _autoSaveTimer.Stop();
        ProfileManager.SaveProfile(_profile);
        RefreshProfileList();
        StatusBannerMessage = $"Saved profile '{_profile.Name}'.";
    }

    public void CreateOrDuplicateProfile()
    {
        string target = string.IsNullOrWhiteSpace(NewProfileName)
            ? $"{_profile.Name} Copy"
            : NewProfileName.Trim();

        _profile.Name = target;
        ProfileManager.SaveProfile(_profile);
        _settings.ActiveProfileName = _profile.Name;
        ProfileManager.SaveSettings(_settings);

        NewProfileName = "";
        _selectedProfileName = _profile.Name;
        RefreshProfileList();
        OnPropertyChanged(nameof(SelectedProfileName));
        StatusBannerMessage = $"Created and switched to profile '{_profile.Name}'.";
    }

    public void DeleteCurrentProfile()
    {
        if (ProfileNames.Count <= 1)
        {
            StatusBannerMessage = "Cannot delete the only remaining profile.";
            return;
        }
        string toDelete = _profile.Name;
        ProfileManager.DeleteProfile(toDelete);
        RefreshProfileList();
        string next = ProfileNames.FirstOrDefault() ?? "Default";
        SelectedProfileName = next;
        StatusBannerMessage = $"Deleted profile '{toDelete}'.";
    }

    public void ImportOfficialProfiles()
    {
        int count = ProfileManager.ImportOfficialGoXlrProfiles();
        RefreshProfileList();
        StatusBannerMessage = $"Imported {count} profile(s) from Documents\\GoXLR.";
    }

    public void ApplyAccentColour(string hex)
    {
        string clean = CleanHex(hex);
        _profile.AccentHexColor = clean;
        _profile.FaderAColor = clean;
        _profile.FaderBColor = clean;
        _profile.FaderCColor = clean;
        _profile.FaderDColor = clean;
        _profile.LightingEffect = "Solid";
        _customHexInput = clean;

        OnPropertyChanged(nameof(AccentHexColor));
        OnPropertyChanged(nameof(FaderAColor));
        OnPropertyChanged(nameof(FaderBColor));
        OnPropertyChanged(nameof(FaderCColor));
        OnPropertyChanged(nameof(FaderDColor));
        OnPropertyChanged(nameof(FaderABrush));
        OnPropertyChanged(nameof(FaderBBrush));
        OnPropertyChanged(nameof(FaderCBrush));
        OnPropertyChanged(nameof(FaderDBrush));
        OnPropertyChanged(nameof(ActiveLightingSummary));
        OnPropertyChanged(nameof(SelectedFaderColorChipBrush));
        OnPropertyChanged(nameof(CustomHexInput));

        RefreshHardwareLighting();
        StatusBannerMessage = clean == "000000" ? "Hardware LEDs turned off." : $"Applied solid #{clean} hardware LED lighting.";
        AutoSaveIfEnabled();
    }

    public void ApplyLightingEffect(string effectName)
    {
        _profile.LightingEffect = effectName;
        OnPropertyChanged(nameof(ActiveLightingSummary));
        RefreshHardwareLighting();
        AutoSaveIfEnabled();
    }

    public void SelectFaderLighting(int index)
    {
        SelectedFaderLightingIndex = index;
    }

    public void ApplyColorToSelectedFader(string hex)
    {
        string clean = CleanHex(hex);
        _customHexInput = clean;
        OnPropertyChanged(nameof(CustomHexInput));

        if (_selectedFaderLightingIndex == 4) // All
        {
            _profile.FaderAColor = clean;
            _profile.FaderBColor = clean;
            _profile.FaderCColor = clean;
            _profile.FaderDColor = clean;
            _profile.AccentHexColor = clean;
            _profile.LightingEffect = "Solid";
            StatusBannerMessage = $"Applied #{clean} across all 4 faders.";
        }
        else
        {
            switch (_selectedFaderLightingIndex)
            {
                case 0: _profile.FaderAColor = clean; break;
                case 1: _profile.FaderBColor = clean; break;
                case 2: _profile.FaderCColor = clean; break;
                case 3: _profile.FaderDColor = clean; break;
            }
            _profile.LightingEffect = "Custom";
            StatusBannerMessage = $"Applied #{clean} to {SelectedFaderLightingTitle}.";
        }

        OnPropertyChanged(nameof(FaderAColor));
        OnPropertyChanged(nameof(FaderBColor));
        OnPropertyChanged(nameof(FaderCColor));
        OnPropertyChanged(nameof(FaderDColor));
        OnPropertyChanged(nameof(FaderABrush));
        OnPropertyChanged(nameof(FaderBBrush));
        OnPropertyChanged(nameof(FaderCBrush));
        OnPropertyChanged(nameof(FaderDBrush));
        OnPropertyChanged(nameof(AccentHexColor));
        OnPropertyChanged(nameof(ActiveLightingSummary));
        OnPropertyChanged(nameof(SelectedFaderColorChipBrush));

        RefreshHardwareLighting();
        AutoSaveIfEnabled();
    }

    public void ApplyCustomHexInput()
    {
        ApplyColorToSelectedFader(CustomHexInput);
    }

    public void ApplyLightingTheme(string themeName)
    {
        _profile.LightingEffect = themeName;
        switch (themeName)
        {
            case "Cyberpunk":
                _profile.FaderAColor = "00FFFF";
                _profile.FaderBColor = "0A84FF";
                _profile.FaderCColor = "A855F7";
                _profile.FaderDColor = "FF2D55";
                StatusBannerMessage = "Applied Theme: Cyberpunk Neon.";
                break;

            case "Sunset":
                _profile.FaderAColor = "A855F7";
                _profile.FaderBColor = "FF2D55";
                _profile.FaderCColor = "FF6414";
                _profile.FaderDColor = "FFC80A";
                StatusBannerMessage = "Applied Theme: Sunset Glow.";
                break;

            case "Ocean":
                _profile.FaderAColor = "003CFF";
                _profile.FaderBColor = "0A84FF";
                _profile.FaderCColor = "00D2FF";
                _profile.FaderDColor = "30D158";
                StatusBannerMessage = "Applied Theme: Ocean Wave.";
                break;

            case "FireIce":
                _profile.FaderAColor = "00FFFF";
                _profile.FaderBColor = "0A84FF";
                _profile.FaderCColor = "FF8C00";
                _profile.FaderDColor = "FF1E1E";
                StatusBannerMessage = "Applied Theme: Fire & Ice.";
                break;

            case "Matrix":
                _profile.FaderAColor = "105020";
                _profile.FaderBColor = "20A040";
                _profile.FaderCColor = "30D158";
                _profile.FaderDColor = "60FF80";
                StatusBannerMessage = "Applied Theme: Matrix Cyber Green.";
                break;

            case "Monochrome":
                _profile.FaderAColor = "50505A";
                _profile.FaderBColor = "9090A0";
                _profile.FaderCColor = "D0D0E0";
                _profile.FaderDColor = "FFFFFF";
                StatusBannerMessage = "Applied Theme: Monochrome Minimal.";
                break;
        }

        OnPropertyChanged(nameof(FaderAColor));
        OnPropertyChanged(nameof(FaderBColor));
        OnPropertyChanged(nameof(FaderCColor));
        OnPropertyChanged(nameof(FaderDColor));
        OnPropertyChanged(nameof(FaderABrush));
        OnPropertyChanged(nameof(FaderBBrush));
        OnPropertyChanged(nameof(FaderCBrush));
        OnPropertyChanged(nameof(FaderDBrush));
        OnPropertyChanged(nameof(ActiveLightingSummary));
        OnPropertyChanged(nameof(SelectedFaderColorChipBrush));

        RefreshHardwareLighting();
        AutoSaveIfEnabled();
    }

    private void RefreshHardwareLighting()
    {
        UpdateAnimationCache();
        if (!_device.IsConnected) return;

        Span<bool> faderMuted = stackalloc bool[4];
        FaderName[] faders = [FaderName.A, FaderName.B, FaderName.C, FaderName.D];
        for (int i = 0; i < 4; i++)
        {
            var ch = _profile.FaderAssignments.GetValueOrDefault(faders[i], ChannelName.Mic);
            faderMuted[i] = _profile.ChannelMuted.GetValueOrDefault(ch, false) || IsChannelCurrentlyDisabled(ch);
        }

        var zA = ParseHexRgb(_profile.FaderAColor);
        var zB = ParseHexRgb(_profile.FaderBColor);
        var zC = ParseHexRgb(_profile.FaderCColor);
        var zD = ParseHexRgb(_profile.FaderDColor);

        PushLightingToDevice(_profile.LightingEffect, zA, zB, zC, zD, faderMuted);
    }

    private void PushLightingToDevice(
        string effectName,
        (byte R, byte G, byte B) zA,
        (byte R, byte G, byte B) zB,
        (byte R, byte G, byte B) zC,
        (byte R, byte G, byte B) zD,
        ReadOnlySpan<bool> faderMuted)
    {
        switch (effectName)
        {
            case "RetroRainbow":
                _device.SetFourZoneMiniLighting(zA, zB, zC, zD, faderMuted);
                _device.SetHardwareAnimationMode(true, 0, 50, 50, 0);
                break;

            case "RainbowBright":
                _device.SetFourZoneMiniLighting(zA, zB, zC, zD, faderMuted);
                _device.SetHardwareAnimationMode(true, 2, 65, 50, 0);
                break;

            case "RainbowDark":
                _device.SetFourZoneMiniLighting(zA, zB, zC, zD, faderMuted);
                _device.SetHardwareAnimationMode(true, 1, 50, 50, 0);
                break;

            case "WaterfallDown":
                _device.SetFourZoneMiniLighting(zA, zB, zC, zD, faderMuted);
                _device.SetHardwareAnimationMode(true, 3, 60, 60, 0);
                break;

            case "WaterfallUp":
                _device.SetFourZoneMiniLighting(zA, zB, zC, zD, faderMuted);
                _device.SetHardwareAnimationMode(true, 3, 60, 60, 1);
                break;

            case "Breathing":
            case "VoiceReactive":
            case "SpectrumSweep":
                _device.SetHardwareAnimationMode(false, 5, 50, 50, 2);
                break;

            default: // "Cyberpunk", "Sunset", "Ocean", "FireIce", "Matrix", "Monochrome", "Solid", "Custom"
                _device.SetHardwareAnimationMode(false, 5, 50, 50, 2);
                _device.SetFourZoneMiniLighting(zA, zB, zC, zD, faderMuted);
                break;
        }
    }

    private static string CleanHex(string? hex, string fallback = "00FFFF")
    {
        string clean = (hex ?? "").Trim().TrimStart('#').ToUpperInvariant();
        return clean.Length == 6 ? clean : fallback;
    }

    private static SolidColorBrush HexToBrush(string hex)
    {
        var (r, g, b) = ParseHexRgb(hex);
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static (byte R, byte G, byte B) ParseHexRgb(string hex)
    {
        string clean = (hex ?? "00FFFF").Trim().TrimStart('#');
        if (clean.Length == 6 &&
            byte.TryParse(clean[0..2], NumberStyles.HexNumber, null, out byte r) &&
            byte.TryParse(clean[2..4], NumberStyles.HexNumber, null, out byte g) &&
            byte.TryParse(clean[4..6], NumberStyles.HexNumber, null, out byte b))
        {
            return (r, g, b);
        }
        return (0, 255, 255);
    }

    private static (byte R, byte G, byte B) HueToRgb(double hue)
    {
        double h = ((hue % 360.0) + 360.0) % 360.0 / 60.0;
        double x = 1.0 - Math.Abs((h % 2.0) - 1.0);
        double r = 0, g = 0, b = 0;
        if (h < 1.0) { r = 1; g = x; b = 0; }
        else if (h < 2.0) { r = x; g = 1; b = 0; }
        else if (h < 3.0) { r = 0; g = 1; b = x; }
        else if (h < 4.0) { r = 0; g = x; b = 1; }
        else if (h < 5.0) { r = x; g = 0; b = 1; }
        else { r = 1; g = 0; b = x; }
        return ((byte)(r * 255.0), (byte)(g * 255.0), (byte)(b * 255.0));
    }

    public void ApplyColorToSelectedFader(byte r, byte g, byte b)
    {
        string hex = $"{r:X2}{g:X2}{b:X2}";
        switch (_selectedFaderLightingIndex)
        {
            case 0: _profile.FaderAColor = hex; break;
            case 1: _profile.FaderBColor = hex; break;
            case 2: _profile.FaderCColor = hex; break;
            case 3: _profile.FaderDColor = hex; break;
            case 4:
                _profile.FaderAColor = hex;
                _profile.FaderBColor = hex;
                _profile.FaderCColor = hex;
                _profile.FaderDColor = hex;
                _profile.AccentHexColor = hex;
                break;
        }
        _profile.LightingEffect = "Custom";
        RefreshHardwareLighting();
        AutoSaveIfEnabled();
        OnPropertyChanged(nameof(FaderABrush));
        OnPropertyChanged(nameof(FaderBBrush));
        OnPropertyChanged(nameof(FaderCBrush));
        OnPropertyChanged(nameof(FaderDBrush));
        OnPropertyChanged(nameof(SelectedFaderColorChipBrush));
        OnPropertyChanged(nameof(ActiveLightingSummary));
    }

    // --- Unified Sidetone & Mute Helpers ---

    private void SetSidetoneState(bool enabled, string? statusMessage = null)
    {
        _profile.MicMonitorDisabled = !enabled;
        _profile.ChannelMuted[ChannelName.MicMonitor] = !enabled;

        if (enabled)
        {
            // Ensure Microphone input and Headphones output are active so sidetone is audible
            _profile.DisabledInputs.Remove(BasicInputDevice.Microphone);
            _profile.DisabledOutputs.Remove(BasicOutputDevice.Headphones);
            _profile.ChannelMuted[ChannelName.Headphones] = false;
            _device.SetChannelMuted(ChannelName.Headphones, false);
            _device.SetChannelVolume(ChannelName.Headphones, _profile.ChannelVolumes.GetValueOrDefault(ChannelName.Headphones, (byte)255));
        }

        if (!_profile.Routing.ContainsKey(BasicInputDevice.Microphone))
        {
            _profile.Routing[BasicInputDevice.Microphone] = new Dictionary<BasicOutputDevice, bool>();
        }
        _profile.Routing[BasicInputDevice.Microphone][BasicOutputDevice.Headphones] = enabled;
        _profile.Routing[BasicInputDevice.Microphone][BasicOutputDevice.BroadcastMix] = enabled;

        byte vol = enabled ? _profile.ChannelVolumes.GetValueOrDefault(ChannelName.MicMonitor, (byte)200) : (byte)0;
        if (enabled && vol == 0)
        {
            vol = 200;
            _profile.ChannelVolumes[ChannelName.MicMonitor] = vol;
        }

        _device.SetChannelVolume(ChannelName.MicMonitor, vol);
        _device.SetChannelMuted(ChannelName.MicMonitor, !enabled);
        PushRoutingForInput(BasicInputDevice.Microphone);

        // Keep Headphone Monitor Engine alive and update sidetone capture state
        if (SelectedPlaybackDevice != null && !SelectedPlaybackDevice.IsGoXlrHardwareDirect)
        {
            _audioMonitorService.UpdateSidetone(enabled);
            if (!_audioMonitorService.IsMonitoring)
            {
                EnsureHeadphoneMonitoring();
            }
        }

        UpdateMicMonitor();
        OnPropertyChanged(nameof(SidetoneEnabled));
        OnPropertyChanged(nameof(SidetoneStatusText));
        OnPropertyChanged(nameof(SidetoneVolumePercent));

        foreach (var chVm in AllChannels)
        {
            if (chVm.Channel == ChannelName.MicMonitor)
            {
                chVm.SetFromHardwareOrProfile(vol, !enabled, !enabled);
            }
        }

        foreach (var slot in FaderSlots)
        {
            if (slot.AssignedChannel == ChannelName.MicMonitor)
            {
                slot.SyncSilent(ChannelName.MicMonitor, vol, !enabled, enabled);
            }
        }

        foreach (var row in RoutingRows)
        {
            if (row.Input == BasicInputDevice.Microphone)
            {
                row.Headphones = enabled;
            }
        }

        UpdateButtonLeds();
        RefreshHardwareLighting();
        AutoSaveIfEnabled();

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            StatusBannerMessage = statusMessage;
        }
    }

    private void SetChannelMuteState(ChannelName ch, bool muted, string? statusMessage = null)
    {
        if (ch == ChannelName.MicMonitor)
        {
            _profile.ChannelMuted[ChannelName.MicMonitor] = muted;
            if (!muted)
            {
                _profile.MicMonitorDisabled = false;
            }

            byte vol = muted ? (byte)0 : _profile.ChannelVolumes.GetValueOrDefault(ChannelName.MicMonitor, (byte)200);
            _device.SetChannelMuted(ChannelName.MicMonitor, muted);
            _device.SetChannelVolume(ChannelName.MicMonitor, vol);

            if (!_profile.Routing.ContainsKey(BasicInputDevice.Microphone))
            {
                _profile.Routing[BasicInputDevice.Microphone] = new Dictionary<BasicOutputDevice, bool>();
            }
            _profile.Routing[BasicInputDevice.Microphone][BasicOutputDevice.Headphones] = !muted && !_profile.MicMonitorDisabled;
            PushRoutingForInput(BasicInputDevice.Microphone);

            OnPropertyChanged(nameof(SidetoneEnabled));
            OnPropertyChanged(nameof(SidetoneStatusText));
            OnPropertyChanged(nameof(SidetoneVolumePercent));

            foreach (var chVm in AllChannels)
            {
                if (chVm.Channel == ChannelName.MicMonitor)
                {
                    chVm.SetFromHardwareOrProfile(vol, muted, _profile.MicMonitorDisabled);
                }
            }

            foreach (var slot in FaderSlots)
            {
                if (slot.AssignedChannel == ChannelName.MicMonitor)
                {
                    slot.SyncSilent(ChannelName.MicMonitor, vol, muted, !_profile.MicMonitorDisabled);
                }
            }

            UpdateButtonLeds();
            RefreshHardwareLighting();
            AutoSaveIfEnabled();

            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                StatusBannerMessage = statusMessage;
            }
            return;
        }

        // If explicitly unmuting a channel that was disabled, automatically re-enable it
        if (!muted && IsChannelCurrentlyDisabled(ch))
        {
            if (TryMapChannelToInput(ch, out var inp))
            {
                _profile.DisabledInputs.Remove(inp);
                PushRoutingForInput(inp);
            }
            else if (TryMapChannelToOutput(ch, out var outp))
            {
                _profile.DisabledOutputs.Remove(outp);
                WindowsAudioEndpointManager.SetRecordingOutputVisibleInWindows(outp, true);
            }
            WindowsAudioEndpointManager.SetPlaybackChannelVisibleInWindows(ch, true);
        }

        _profile.ChannelMuted[ch] = muted;
        bool effectivelyMuted = muted || IsChannelCurrentlyDisabled(ch);
        byte effectiveVol = effectivelyMuted ? (byte)0 : _profile.ChannelVolumes.GetValueOrDefault(ch, (byte)255);

        _device.SetChannelMuted(ch, effectivelyMuted);
        _device.SetChannelVolume(ch, effectiveVol);

        if (ch == ChannelName.Mic)
        {
            _device.SetEffectParams([(EffectKey.MicInputMute, effectivelyMuted ? 1 : 0)]);
            PushRoutingForInput(BasicInputDevice.Microphone);
        }

        UpdateButtonLeds();
        RefreshHardwareLighting();
        SyncViewModelsFromProfile();
        AutoSaveIfEnabled();

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            StatusBannerMessage = statusMessage;
        }
    }

    // --- Private helpers ---

    private void LoadAndApplyProfile(string name)
    {
        _profile = ProfileManager.LoadProfile(name);
        _settings.ActiveProfileName = _profile.Name;
        ProfileManager.SaveSettings(_settings);
        RefreshProfileList();
        SyncViewModelsFromProfile();
        ApplyFullProfileToHardware(applyStartupLevelRule: false);
        StatusBannerMessage = $"Loaded profile '{_profile.Name}'.";
    }

    private void RefreshProfileList()
    {
        var names = ProfileManager.ListProfileNames();
        if (names.Count == 0)
        {
            ProfileManager.SaveProfile(new XlrProfile { Name = "Default" });
            names = ProfileManager.ListProfileNames();
        }
        ProfileNames.Clear();
        ProfileItems.Clear();
        ProfileCards.Clear();
        foreach (var n in names)
        {
            ProfileNames.Add(n);
            ProfileItems.Add(new ProfileChipViewModel(n, string.Equals(n, _selectedProfileName, StringComparison.OrdinalIgnoreCase)));
            var (badge, desc) = ProfileManager.GetProfileInfo(n);
            ProfileCards.Add(new ProfileCardViewModel(n, badge, desc, string.Equals(n, _selectedProfileName, StringComparison.OrdinalIgnoreCase)));
        }
        OnPropertyChanged(nameof(SelectedProfileName));
    }

    private void SyncViewModelsFromProfile()
    {
        _suppressApply = true;

        foreach (var slot in FaderSlots)
        {
            var ch = _profile.FaderAssignments.GetValueOrDefault(slot.Fader, ChannelName.Mic);
            byte vol = _profile.ChannelVolumes.GetValueOrDefault(ch, (byte)255);
            bool muted = _profile.ChannelMuted.GetValueOrDefault(ch, false);
            bool disabled = IsChannelCurrentlyDisabled(ch);
            slot.SyncSilent(ch, vol, muted, !disabled);
        }

        foreach (var chVm in AllChannels)
        {
            byte vol = _profile.ChannelVolumes.GetValueOrDefault(chVm.Channel, (byte)255);
            bool muted = _profile.ChannelMuted.GetValueOrDefault(chVm.Channel, false);
            bool disabled = IsChannelCurrentlyDisabled(chVm.Channel);
            chVm.SetFromHardwareOrProfile(vol, muted, disabled);

            var assignedFaders = _profile.FaderAssignments
                .Where(kv => kv.Value == chVm.Channel)
                .Select(kv => $"Fader {kv.Key}")
                .ToList();
            chVm.IsAssignedToPhysicalFader = assignedFaders.Count > 0;
            chVm.AssignedFaderBadge = assignedFaders.Count > 0 ? string.Join(", ", assignedFaders) : "Background";
        }

        ActiveRoutingRows.Clear();
        InactiveRoutingRows.Clear();
        foreach (var row in RoutingRows)
        {
            var map = _profile.Routing.GetValueOrDefault(row.Input) ?? new Dictionary<BasicOutputDevice, bool>();
            bool isDisabled = _profile.DisabledInputs.Contains(row.Input);
            var ch = MapInputToChannel(row.Input);
            byte rawVol = _profile.ChannelVolumes.GetValueOrDefault(ch, (byte)255);
            row.VolumePercent = (int)Math.Round(rawVol / 255.0 * 100.0);
            row.IsMuted = _profile.ChannelMuted.GetValueOrDefault(ch, false);
            row.LoadFromProfile(map, isDisabled);
            if (!isDisabled)
            {
                ActiveRoutingRows.Add(row);
            }
            else
            {
                InactiveRoutingRows.Add(row);
            }
        }
        OnPropertyChanged(nameof(HasInactiveRoutingRows));
        OnPropertyChanged(nameof(IsMusicMutedOnStream));
        OnPropertyChanged(nameof(IsChatPrivateInHeadset));
        OnPropertyChanged(nameof(HeadphonesConnectedSummary));
        OnPropertyChanged(nameof(StreamMixConnectedSummary));
        OnPropertyChanged(nameof(ChatMicConnectedSummary));
        OnPropertyChanged(nameof(LineOutConnectedSummary));

        OnPropertyChanged(nameof(IsHeadphonesOutputEnabled));
        OnPropertyChanged(nameof(IsBroadcastOutputEnabled));
        OnPropertyChanged(nameof(IsChatMicOutputEnabled));
        OnPropertyChanged(nameof(IsSamplerOutputEnabled));
        OnPropertyChanged(nameof(IsLineOutOutputEnabled));

        OnPropertyChanged(nameof(IsMicMuted));
        OnPropertyChanged(nameof(MicMuteStatusText));
        OnPropertyChanged(nameof(BleepLevelDb));
        OnPropertyChanged(nameof(BleepVolumePercent));
        OnPropertyChanged(nameof(MicType));
        OnPropertyChanged(nameof(IsDynamicMic));
        OnPropertyChanged(nameof(IsCondenserMic));
        OnPropertyChanged(nameof(IsJackMic));
        OnPropertyChanged(nameof(MicGainDb));
        OnPropertyChanged(nameof(MicGainActiveWidth));
        OnPropertyChanged(nameof(MicGainRemainingWidth));
        OnPropertyChanged(nameof(SidetoneEnabled));
        OnPropertyChanged(nameof(SidetoneStatusText));
        OnPropertyChanged(nameof(SidetoneVolumePercent));
        OnPropertyChanged(nameof(SidetoneActiveWidth));
        OnPropertyChanged(nameof(SidetoneRemainingWidth));
        OnPropertyChanged(nameof(AccentHexColor));
        OnPropertyChanged(nameof(ActiveLightingSummary));
        OnPropertyChanged(nameof(GateEnabled));
        OnPropertyChanged(nameof(GateStatusText));
        OnPropertyChanged(nameof(GateSimpleSummaryText));
        OnPropertyChanged(nameof(GateSimpleMode));
        OnPropertyChanged(nameof(GateAdvancedMode));
        OnPropertyChanged(nameof(GateSimpleVisibility));
        OnPropertyChanged(nameof(GateAdvancedVisibility));
        OnPropertyChanged(nameof(GateSimpleAmount));
        OnPropertyChanged(nameof(GateThresholdDb));
        OnPropertyChanged(nameof(GateThresholdPercentOnMeter));
        OnPropertyChanged(nameof(GateCutoffMeterPercent));
        OnPropertyChanged(nameof(GateCutoffActiveWidth));
        OnPropertyChanged(nameof(GateCutoffRemainingWidth));
        OnPropertyChanged(nameof(GateCutoffLabel));
        OnPropertyChanged(nameof(GateAttenuationPercent));
        OnPropertyChanged(nameof(GateAttackIndex));
        OnPropertyChanged(nameof(GateReleaseIndex));
        OnPropertyChanged(nameof(CompressorEnabled));
        OnPropertyChanged(nameof(CompressorSimpleSummaryText));
        OnPropertyChanged(nameof(CompressorSimpleMode));
        OnPropertyChanged(nameof(CompressorAdvancedMode));
        OnPropertyChanged(nameof(CompressorSimpleVisibility));
        OnPropertyChanged(nameof(CompressorAdvancedVisibility));
        OnPropertyChanged(nameof(CompressorSimpleAmount));
        OnPropertyChanged(nameof(CompressorThresholdDb));
        OnPropertyChanged(nameof(CompressorRatioIndex));
        OnPropertyChanged(nameof(CompressorAttackIndex));
        OnPropertyChanged(nameof(CompressorReleaseIndex));
        OnPropertyChanged(nameof(CompressorMakeUpGainDb));
        OnPropertyChanged(nameof(DeEsserEnabled));
        OnPropertyChanged(nameof(DeEsserPercent));
        OnPropertyChanged(nameof(DeEsserSimpleSummaryText));
        OnPropertyChanged(nameof(EqEnabled));
        OnPropertyChanged(nameof(EqSimpleSummaryText));
        OnPropertyChanged(nameof(EqSimpleMode));
        OnPropertyChanged(nameof(EqAdvancedMode));
        OnPropertyChanged(nameof(EqSimpleVisibility));
        OnPropertyChanged(nameof(EqAdvancedVisibility));
        OnPropertyChanged(nameof(EqSimpleBassGain));
        OnPropertyChanged(nameof(EqSimpleMidGain));
        OnPropertyChanged(nameof(EqSimpleTrebleGain));
        OnPropertyChanged(nameof(Eq90HzGain));
        OnPropertyChanged(nameof(Eq250HzGain));
        OnPropertyChanged(nameof(Eq500HzGain));
        OnPropertyChanged(nameof(Eq1KHzGain));
        OnPropertyChanged(nameof(Eq3KHzGain));
        OnPropertyChanged(nameof(Eq8KHzGain));

        OnPropertyChanged(nameof(StudioNoiseSuppressionEnabled));
        OnPropertyChanged(nameof(StudioNoiseSuppressionPreset));
        OnPropertyChanged(nameof(StudioNoiseSuppressionStatusText));
        OnPropertyChanged(nameof(StudioNoiseSuppressionPresetLabel));
        OnPropertyChanged(nameof(IsPresetStudioClean));
        OnPropertyChanged(nameof(IsPresetKeyboardRejection));
        OnPropertyChanged(nameof(IsPresetRawStudio));
        OnPropertyChanged(nameof(ProfileVocalTonePercent));
        OnPropertyChanged(nameof(ProfileVocalToneLabel));
        OnPropertyChanged(nameof(ProfileNoiseRejectionPercent));
        OnPropertyChanged(nameof(ProfileNoiseRejectionLabel));

        OnPropertyChanged(nameof(FaderAColor));
        OnPropertyChanged(nameof(FaderBColor));
        OnPropertyChanged(nameof(FaderCColor));
        OnPropertyChanged(nameof(FaderDColor));
        OnPropertyChanged(nameof(FaderABrush));
        OnPropertyChanged(nameof(FaderBBrush));
        OnPropertyChanged(nameof(FaderCBrush));
        OnPropertyChanged(nameof(FaderDBrush));
        OnPropertyChanged(nameof(SelectedFaderLightingTitle));
        OnPropertyChanged(nameof(SelectedFaderColorChipBrush));
        NotifyVoiceShaperProperties();

        // Sync Headphone Sound Profile (Sennheiser HD 560S / USB DAC Equalizer)
        _headphonePreset = string.IsNullOrWhiteSpace(_profile.HeadphonePreset) ? "Harman" : _profile.HeadphonePreset;
        _headphoneSubBassDb = _profile.HeadphoneSubBassDb;
        _headphoneWarmthDb = _profile.HeadphoneWarmthDb;
        _headphonePresenceDb = _profile.HeadphonePresenceDb;
        _headphoneAirDb = _profile.HeadphoneAirDb;

        OnPropertyChanged(nameof(HeadphonePreset));
        OnPropertyChanged(nameof(IsHeadphoneHarman));
        OnPropertyChanged(nameof(IsHeadphoneBassBoost));
        OnPropertyChanged(nameof(IsHeadphoneGaming));
        OnPropertyChanged(nameof(IsHeadphoneVocalWarm));
        OnPropertyChanged(nameof(IsHeadphoneFlat));
        OnPropertyChanged(nameof(HeadphonePresetSummaryText));
        OnPropertyChanged(nameof(HeadphoneSubBassDb));
        OnPropertyChanged(nameof(HeadphoneWarmthDb));
        OnPropertyChanged(nameof(HeadphonePresenceDb));
        OnPropertyChanged(nameof(HeadphoneAirDb));
        OnPropertyChanged(nameof(HeadphoneSubBassLabel));
        OnPropertyChanged(nameof(HeadphoneWarmthLabel));
        OnPropertyChanged(nameof(HeadphonePresenceLabel));
        OnPropertyChanged(nameof(HeadphoneAirLabel));

        _audioMonitorService.ApplyHeadphoneProfile(_headphonePreset, _headphoneSubBassDb, _headphoneWarmthDb, _headphonePresenceDb, _headphoneAirDb);

        _suppressApply = false;
        RaiseRoutingGraphChanged();
    }

    private void ProcessHardwareState(HardwareButtonAndFaderState state)
    {
        bool faderChanged = false;

        // 1. Update volumes from the 4 physical faders in real time
        foreach (var fader in Enum.GetValues<FaderName>())
        {
            int idx = (int)fader;
            byte hwVol = state.GetFaderVolume(fader);

            // Mini reports 0 for a fader until it first moves: don't treat that as a real position.
            if (_faderUnreported[idx])
            {
                if (hwVol == 0) { _faderLastSeen[idx] = 0; continue; }
                _faderUnreported[idx] = false;
            }

            // If this fader was paused because of a software UI adjustment, ignore stationary hardware ADC values
            if (_faderPaused[idx])
            {
                // Only unpause if the user actually pushed the physical slider by more than 3 ADC counts
                if (Math.Abs((int)hwVol - (int)_faderPauseReferenceHwVol[idx]) >= 3)
                {
                    _faderPaused[idx] = false;
                }
                else
                {
                    _faderLastSeen[idx] = hwVol;
                    continue;
                }
            }

            var ch = _profile.FaderAssignments.GetValueOrDefault(fader, ChannelName.Mic);
            byte currentSoftwareVol = _profile.ChannelVolumes.GetValueOrDefault(ch);

            if (!_pendingFaderSync && hwVol == _faderLastSeen[idx] && currentSoftwareVol == hwVol)
            {
                continue;
            }
            _faderLastSeen[idx] = hwVol;

            if (currentSoftwareVol != hwVol)
            {
                _profile.ChannelVolumes[ch] = hwVol;

                // On GoXLR Mini, physical sliders directly adjust channel DSP volume in hardware.
                // Do NOT call SetChannelVolume over USB here: sending USB vendor commands during physical fader
                // motion floods the endpoint and locks up the hardware ADC polling stream!

                // Smoothly update the corresponding viewmodels directly without expensive full viewmodel re-sync
                foreach (var slot in FaderSlots)
                {
                    if (slot.Fader == fader)
                    {
                        slot.SyncSilent(ch, hwVol, slot.IsMuted, slot.IsChannelEnabled);
                    }
                }

                foreach (var chVm in AllChannels)
                {
                    if (chVm.Channel == ch)
                    {
                        chVm.SetFromHardwareOrProfile(hwVol, chVm.IsMuted, chVm.IsDisabled);
                    }
                }

                if (ch == ChannelName.MicMonitor)
                {
                    OnPropertyChanged(nameof(SidetoneVolumePercent));
                    OnPropertyChanged(nameof(SidetoneStatusText));
                }
                else if (ch == ChannelName.Headphones)
                {
                    // If an external DAC (like AudioQuest DragonFly Red) is active, do not let an unused physical 0% fader mute the audio
                    if (SelectedPlaybackDevice != null && !SelectedPlaybackDevice.IsGoXlrHardwareDirect && hwVol <= 0)
                    {
                        _audioMonitorService.SetVolume(100.0f);
                    }
                    else
                    {
                        _audioMonitorService.SetVolume((float)(hwVol / 255.0 * 100.0));
                    }
                }

                faderChanged = true;
            }
        }

        _pendingFaderSync = false;

        if (faderChanged)
        {
            AutoSaveIfEnabled();
        }

        // 2. Check physical buttons (Fader 1..4 Mute = bits 4, 9, 14, 19; Bleep = bit 22; Cough/Mic Mute = bit 23)
        uint newlyPressed = state.PressedButtonsMask & ~_lastButtonsMask;
        uint newlyReleased = _lastButtonsMask & ~state.PressedButtonsMask;
        _lastButtonsMask = state.PressedButtonsMask;

        ReadOnlySpan<(int Bit, FaderName Fader)> faderMuteBits =
        [
            (4, FaderName.A),
            (9, FaderName.B),
            (14, FaderName.C),
            (19, FaderName.D)
        ];

        foreach (var (bit, fader) in faderMuteBits)
        {
            if (((newlyPressed >> bit) & 1) != 0)
            {
                var ch = _profile.FaderAssignments.GetValueOrDefault(fader, ChannelName.Mic);
                bool currentlyMuted = _profile.ChannelMuted.GetValueOrDefault(ch, false) || IsChannelCurrentlyDisabled(ch);
                bool newMuted = !currentlyMuted;
                SetChannelMuteState(
                    ch,
                    newMuted,
                    $"Fader {fader} button pressed → {DspTables.GetChannelDisplayName(ch)} {(newMuted ? "Muted" : "Unmuted")}");
            }
        }

        // Bleep (#!*@) button (bit 22) - momentary censor beep while held
        if (((newlyPressed >> 22) & 1) != 0)
        {
            IsBleepHeld = true;
        }
        else if (((newlyReleased >> 22) & 1) != 0)
        {
            IsBleepHeld = false;
        }

        // Cough / Mic Mute button (bit 23) - supports both tap-to-toggle and hold-to-cough
        if (((newlyPressed >> 23) & 1) != 0)
        {
            _coughPressStartUtc = DateTime.UtcNow;
            bool wasMuted = _profile.ChannelMuted.GetValueOrDefault(ChannelName.Mic, false);
            _coughMutedOnPress = !wasMuted;
            SetChannelMuteState(
                ChannelName.Mic,
                !wasMuted,
                !wasMuted ? "Cough button pressed → Microphone Muted" : "Cough button pressed → Microphone Unmuted");
        }
        else if (((newlyReleased >> 23) & 1) != 0)
        {
            // If held for > 350ms after muting on press, treat as momentary hold-to-cough and unmute on release
            if (_coughMutedOnPress && (DateTime.UtcNow - _coughPressStartUtc).TotalMilliseconds > 350)
            {
                SetChannelMuteState(ChannelName.Mic, false, "Cough button released → Microphone Live");
            }
        }
    }

    private async Task RunHardwarePollLoopAsync(CancellationToken ct)
    {
        // Trigger initial hardware connect immediately on worker startup
        if (!_device.IsConnected)
        {
            _lastReconnectAttemptTick = Environment.TickCount64;
            await ReconnectDeviceAsync(ct).ConfigureAwait(false);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(30));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            if (_isDisposed) break;

            if (!_device.IsConnected)
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    if (_isDisposed || !IsConnected) return;
                    IsConnected = false;
                    ConnectionStatusText = "Connecting to GoXLR Mini...";
                    OnPropertyChanged(nameof(DeviceModelText));
                    OnPropertyChanged(nameof(DeviceFirmwareText));
                    OnPropertyChanged(nameof(DeviceSerialText));
                }, DispatcherPriority.DataBind, ct);

                long tickNow = Environment.TickCount64;
                if (tickNow - _lastReconnectAttemptTick >= 1500)
                {
                    _lastReconnectAttemptTick = tickNow;
                    await ReconnectDeviceAsync(ct).ConfigureAwait(false);
                }
                continue;
            }

            try
            {
                // 1. Poll physical faders, buttons, and mic dB meter on background thread (0 UI thread blocking!)
                var hwState = _device.GetButtonStates();
                double instantDb = _device.IsConnected ? _device.GetMicrophoneLevelDb() : -72.2;

                // 2. Drive software LED animations on background thread if enabled
                DriveSoftwareLightingTick(instantDb);

                // 3. Marshal lightweight state snapshot to UI thread
                await _uiDispatcher.InvokeAsync(
                    () => ApplyPolledTickOnUi(hwState, instantDb),
                    DispatcherPriority.DataBind,
                    ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore transient poll errors
            }
        }
    }

    private void DriveSoftwareLightingTick(double instantDb)
    {
        string effect = _cachedLightingEffect;
        if (effect != "Breathing" && effect != "VoiceReactive" && effect != "SpectrumSweep")
        {
            return;
        }

        int mask = _cachedFaderMutedMask;
        Span<bool> faderMuted =
        [
            (mask & 1) != 0,
            (mask & 2) != 0,
            (mask & 4) != 0,
            (mask & 8) != 0
        ];

        if (effect == "Breathing")
        {
            _animPhase += 0.10;
            double factor = 0.15 + 0.85 * ((Math.Sin(_animPhase) + 1.0) * 0.5);
            var baseRgb = ParseHexRgb(_cachedAccentHex);
            var c = ((byte)(baseRgb.R * factor), (byte)(baseRgb.G * factor), (byte)(baseRgb.B * factor));
            _device.SetFourZoneMiniLighting(c, c, c, c, faderMuted);
        }
        else if (effect == "VoiceReactive")
        {
            double norm = Math.Clamp((instantDb + 55.0) / 45.0, 0.0, 1.0);
            (byte R, byte G, byte B) color;
            if (norm > 0.85) color = (255, 60, 40);
            else if (norm > 0.55) color = (255, 200, 10);
            else if (norm > 0.15) color = (0, 220, 160);
            else
            {
                var baseRgb = ParseHexRgb(_cachedAccentHex);
                color = ((byte)(baseRgb.R * 0.15), (byte)(baseRgb.G * 0.15), (byte)(baseRgb.B * 0.15));
            }
            _device.SetFourZoneMiniLighting(color, color, color, color, faderMuted);
        }
        else if (effect == "SpectrumSweep")
        {
            _animPhase += 0.08;
            var c1 = HueToRgb((_animPhase * 57.2958) % 360.0);
            var c2 = HueToRgb(((_animPhase * 57.2958) + 90.0) % 360.0);
            var c3 = HueToRgb(((_animPhase * 57.2958) + 180.0) % 360.0);
            var c4 = HueToRgb(((_animPhase * 57.2958) + 270.0) % 360.0);
            _device.SetFourZoneMiniLighting(c1, c2, c3, c4, faderMuted);
        }
    }

    private void ApplyPolledTickOnUi(HardwareButtonAndFaderState? hwState, double instantDb)
    {
        if (_isDisposed || !_device.IsConnected) return;

        RecordVoiceCheckSample(instantDb);
        UpdateVoiceAssist(hwState, instantDb);

        if (hwState != null)
        {
            ProcessHardwareState(hwState);
        }

        if (Math.Abs(instantDb - _micLevelDb) >= 0.05)
        {
            MicLevelDb = instantDb;
        }

        if (instantDb > _obsSmoothDb)
        {
            _obsSmoothDb = instantDb;
        }
        else
        {
            // Smooth audio ballistics decay (~1.8 dB per 30ms tick = ~60 dB/sec decay)
            _obsSmoothDb = Math.Max(-60.0, _obsSmoothDb - 1.8);
        }

        long now = Environment.TickCount64;
        if (instantDb >= _obsPeakHoldDb)
        {
            _obsPeakHoldDb = instantDb;
            _peakHoldExpiryTick = now + 1200; // 1.2s peak hold
        }
        else if (now >= _peakHoldExpiryTick)
        {
            _obsPeakHoldDb = Math.Max(_obsSmoothDb, _obsPeakHoldDb - 2.5);
        }

        // Only invalidate WPF meter layout & bindings when dB values meaningfully move (0% idle churn in silence!)
        if (double.IsNaN(_lastNotifiedSmoothDb) ||
            Math.Abs(_obsSmoothDb - _lastNotifiedSmoothDb) >= 0.05 ||
            Math.Abs(_obsPeakHoldDb - _lastNotifiedPeakDb) >= 0.05)
        {
            _lastNotifiedSmoothDb = _obsSmoothDb;
            _lastNotifiedPeakDb = _obsPeakHoldDb;

            OnPropertyChanged(nameof(ObsLevelDb));
            OnPropertyChanged(nameof(ObsPeakHoldDb));
            OnPropertyChanged(nameof(ObsLevelPercent));
            OnPropertyChanged(nameof(ObsPeakHoldPercent));
            OnPropertyChanged(nameof(ObsMeterActiveWidth));
            OnPropertyChanged(nameof(ObsMeterRemainingWidth));
            OnPropertyChanged(nameof(ObsPeakHoldActiveWidth));
            OnPropertyChanged(nameof(ObsPeakHoldRemainingWidth));
            OnPropertyChanged(nameof(IsClipping));
            OnPropertyChanged(nameof(ObsPeakHoldText));
            OnPropertyChanged(nameof(ObsLevelText));
            OnPropertyChanged(nameof(ObsMeterBrush));
        }

        _obsStatusPeakDb = Math.Max(_obsStatusPeakDb, _obsSmoothDb);
        if (now >= _obsStatusNextTick)
        {
            _obsStatusNextTick = now + 500;
            _obsStatusDb = _obsStatusPeakDb;
            _obsStatusPeakDb = _obsSmoothDb;
            OnPropertyChanged(nameof(ObsLevelStatusText));
            OnPropertyChanged(nameof(ObsMeterStatusBrush));
        }
    }

    private bool IsChannelCurrentlyDisabled(ChannelName ch)
    {
        if (ch == ChannelName.MicMonitor)
        {
            return _profile.MicMonitorDisabled;
        }
        bool disabledIn = TryMapChannelToInput(ch, out var inp) && _profile.DisabledInputs.Contains(inp);
        bool disabledOut = TryMapChannelToOutput(ch, out var outp) && _profile.DisabledOutputs.Contains(outp);
        return disabledIn || disabledOut;
    }

    private void SetChannelEnabled(ChannelName ch, bool enabled)
    {
        if (ch == ChannelName.MicMonitor)
        {
            SetSidetoneState(enabled, enabled ? "Mic Monitor (Sidetone) Enabled" : "Mic Monitor (Sidetone) Disabled");
            return;
        }

        if (TryMapChannelToInput(ch, out var inp))
        {
            if (!enabled)
            {
                if (!_profile.DisabledInputs.Contains(inp)) _profile.DisabledInputs.Add(inp);
            }
            else
            {
                _profile.DisabledInputs.Remove(inp);
            }
            PushRoutingForInput(inp);
        }
        else if (TryMapChannelToOutput(ch, out var outp))
        {
            SetOutputEnabled(outp, enabled);
            return;
        }

        bool muted = !enabled || _profile.ChannelMuted.GetValueOrDefault(ch, false);
        byte vol = muted ? (byte)0 : _profile.ChannelVolumes.GetValueOrDefault(ch, (byte)255);
        _device.SetChannelVolume(ch, vol);
        _device.SetChannelMuted(ch, muted);
        WindowsAudioEndpointManager.SetPlaybackChannelVisibleInWindows(ch, enabled);

        UpdateButtonLeds();
        RefreshHardwareLighting();
        SyncViewModelsFromProfile();
        AutoSaveIfEnabled();
        StatusBannerMessage = $"{DspTables.GetChannelDisplayName(ch)} {(enabled ? "enabled in Windows Sound & Routing" : "disabled & hidden from Windows Sound")}.";
    }

    private void OnFaderEnabledChanged(FaderSlotViewModel slot)
    {
        if (_suppressApply) return;
        SetChannelEnabled(slot.AssignedChannel, slot.IsChannelEnabled);
    }

    private void OnChannelEnabledChanged(ChannelMixerItemViewModel item)
    {
        if (_suppressApply) return;
        SetChannelEnabled(item.Channel, item.IsChannelEnabled);
    }

    private void OnFaderAssignmentChanged(FaderSlotViewModel slot)
    {
        if (_suppressApply) return;
        _profile.FaderAssignments[slot.Fader] = slot.AssignedChannel;
        _device.SetFaderChannel(slot.Fader, slot.AssignedChannel);
        _device.SetFaderDisplayMode(slot.Fader, gradient: false, meter: false);
        // Automatically enable a channel when you assign it to a physical fader
        SetChannelEnabled(slot.AssignedChannel, true);
    }

    private void OnFaderVolumeChanged(FaderSlotViewModel slot)
    {
        if (_suppressApply) return;
        byte raw = (byte)Math.Round(slot.VolumePercent / 100.0 * 255.0);
        _profile.ChannelVolumes[slot.AssignedChannel] = raw;

        // Pause hardware polling from fighting this UI slider adjustment
        int faderIdx = (int)slot.Fader;
        _faderPaused[faderIdx] = true;
        _faderPauseReferenceHwVol[faderIdx] = _faderLastSeen[faderIdx];

        foreach (var f in Enum.GetValues<FaderName>())
        {
            if (_profile.FaderAssignments.GetValueOrDefault(f) == slot.AssignedChannel)
            {
                _faderPaused[(int)f] = true;
                _faderPauseReferenceHwVol[(int)f] = _faderLastSeen[(int)f];
            }
        }

        if (!IsChannelCurrentlyDisabled(slot.AssignedChannel) && !_profile.ChannelMuted.GetValueOrDefault(slot.AssignedChannel, false))
        {
            _device.SetChannelVolume(slot.AssignedChannel, raw);
        }

        // Direct sync to AllChannels VM without recreating viewmodel trees
        foreach (var chVm in AllChannels)
        {
            if (chVm.Channel == slot.AssignedChannel)
            {
                chVm.SetFromHardwareOrProfile(raw, chVm.IsMuted, chVm.IsDisabled);
            }
        }

        if (slot.AssignedChannel == ChannelName.MicMonitor)
        {
            OnPropertyChanged(nameof(SidetoneVolumePercent));
            OnPropertyChanged(nameof(SidetoneStatusText));
        }
        else if (slot.AssignedChannel == ChannelName.Headphones)
        {
            if (SelectedPlaybackDevice != null && !SelectedPlaybackDevice.IsGoXlrHardwareDirect && slot.VolumePercent <= 0)
            {
                _audioMonitorService.SetVolume(100.0f);
            }
            else
            {
                _audioMonitorService.SetVolume(slot.VolumePercent);
            }
        }

        AutoSaveIfEnabled();
    }

    private void OnFaderMuteChanged(FaderSlotViewModel slot)
    {
        if (_suppressApply) return;
        SetChannelMuteState(
            slot.AssignedChannel,
            slot.IsMuted,
            $"{DspTables.GetChannelDisplayName(slot.AssignedChannel)} {(slot.IsMuted ? "Muted" : "Unmuted")}");
    }

    private void OnChannelVolumeChanged(ChannelMixerItemViewModel item)
    {
        if (_suppressApply) return;
        _profile.ChannelVolumes[item.Channel] = item.VolumeRaw;

        // Pause any physical fader assigned to this channel
        foreach (var f in Enum.GetValues<FaderName>())
        {
            if (_profile.FaderAssignments.GetValueOrDefault(f) == item.Channel)
            {
                _faderPaused[(int)f] = true;
                _faderPauseReferenceHwVol[(int)f] = _faderLastSeen[(int)f];
            }
        }

        if (!IsChannelCurrentlyDisabled(item.Channel) && !_profile.ChannelMuted.GetValueOrDefault(item.Channel, false))
        {
            _device.SetChannelVolume(item.Channel, item.VolumeRaw);
        }

        // Direct sync to FaderSlots VM
        foreach (var slot in FaderSlots)
        {
            if (slot.AssignedChannel == item.Channel)
            {
                slot.SyncSilent(slot.AssignedChannel, item.VolumeRaw, slot.IsMuted, slot.IsChannelEnabled);
            }
        }

        if (item.Channel == ChannelName.MicMonitor)
        {
            OnPropertyChanged(nameof(SidetoneVolumePercent));
            OnPropertyChanged(nameof(SidetoneStatusText));
        }
        else if (item.Channel == ChannelName.Headphones)
        {
            if (SelectedPlaybackDevice != null && !SelectedPlaybackDevice.IsGoXlrHardwareDirect && item.VolumePercent <= 0)
            {
                _audioMonitorService.SetVolume(100.0f);
            }
            else
            {
                _audioMonitorService.SetVolume(item.VolumePercent);
            }
        }

        AutoSaveIfEnabled();
    }

    private void OnChannelMuteChanged(ChannelMixerItemViewModel item)
    {
        if (_suppressApply) return;
        SetChannelMuteState(
            item.Channel,
            item.IsMuted,
            $"{DspTables.GetChannelDisplayName(item.Channel)} {(item.IsMuted ? "Muted" : "Unmuted")}");
    }

    private void OnRouteChanged(BasicInputDevice input, BasicOutputDevice output, bool enabled)
    {
        if (_suppressApply) return;
        if (!_profile.Routing.ContainsKey(input))
            _profile.Routing[input] = new Dictionary<BasicOutputDevice, bool>();

        _profile.Routing[input][output] = enabled;

        // Keep Sidetone (Mic Monitor) in sync if user toggles Microphone -> Headphones in Routing
        if (input == BasicInputDevice.Microphone && output == BasicOutputDevice.Headphones)
        {
            SetSidetoneState(enabled, enabled ? "Routed Microphone to Headphones (Sidetone Active)" : "Unrouted Microphone from Headphones (Sidetone Off)");
            return;
        }

        PushRoutingForInput(input);
        SyncViewModelsFromProfile();
        AutoSaveIfEnabled();
        StatusBannerMessage = $"{DspTables.GetInputDisplayName(input)} → {DspTables.GetOutputDisplayName(output)} {(enabled ? "connected" : "disconnected")}.";
    }

    private void OnInputDisabledChanged(BasicInputDevice input, bool disabled)
    {
        if (_suppressApply) return;
        var ch = MapInputToChannel(input);
        SetChannelEnabled(ch, !disabled);
    }

    public void SetInputEnabled(BasicInputDevice input, bool enabled)
    {
        if (input == BasicInputDevice.Microphone) return;
        var ch = MapInputToChannel(input);
        SetChannelEnabled(ch, enabled);
        NotifyChannelEnabledProperties();
    }

    public void SetOutputEnabled(BasicOutputDevice output, bool enabled)
    {
        // Protect BroadcastMix from ever being disabled (master capture source)
        if (output == BasicOutputDevice.BroadcastMix)
        {
            enabled = true;
        }

        if (!enabled)
        {
            if (!_profile.DisabledOutputs.Contains(output)) _profile.DisabledOutputs.Add(output);
        }
        else
        {
            _profile.DisabledOutputs.Remove(output);
        }

        if (TryMapOutputToChannel(output, out var ch))
        {
            bool muted = !enabled || _profile.ChannelMuted.GetValueOrDefault(ch, false);
            _device.SetChannelMuted(ch, muted);
            _device.SetChannelVolume(ch, muted ? (byte)0 : _profile.ChannelVolumes.GetValueOrDefault(ch, (byte)255));
        }

        WindowsAudioEndpointManager.SetRecordingOutputVisibleInWindows(output, enabled);

        foreach (var inp in Enum.GetValues<BasicInputDevice>())
        {
            PushRoutingForInput(inp);
        }

        UpdateButtonLeds();
        RefreshHardwareLighting();
        SyncViewModelsFromProfile();
        AutoSaveIfEnabled();
        NotifyChannelEnabledProperties();
        StatusBannerMessage = $"{DspTables.GetOutputDisplayName(output)} {(enabled ? "enabled" : "disabled & hidden from Windows Sound")}.";
    }

    private Dictionary<BasicOutputDevice, bool> ComputeEffectiveRoutingForInput(BasicInputDevice input)
    {
        var effective = new Dictionary<BasicOutputDevice, bool>();
        bool inputDisabled = _profile.DisabledInputs.Contains(input);
        bool micMuted = input == BasicInputDevice.Microphone && _profile.ChannelMuted.GetValueOrDefault(ChannelName.Mic, false);

        if (!inputDisabled && !micMuted && _profile.Routing.TryGetValue(input, out var row))
        {
            foreach (var kv in row)
            {
                bool outputDisabled = _profile.DisabledOutputs.Contains(kv.Key);
                if (kv.Key == BasicOutputDevice.BroadcastMix || kv.Key == BasicOutputDevice.Sampler || kv.Key == BasicOutputDevice.ChatMic)
                {
                    outputDisabled = false;
                }
                effective[kv.Key] = kv.Value && !outputDisabled;
            }

            if (input == BasicInputDevice.Microphone)
            {
                bool hwSidetone = SidetoneEnabled && !IsMicCheckActive;

                // GoXLR 3.5mm hardware headphone output
                effective[BasicOutputDevice.Headphones] = hwSidetone;

                // Respect BroadcastMix routing while suppressing voice loopback during active Mic Check or external DAC monitor
                bool broadcastRequested = _audioMonitorService.IsMonitoring
                    ? hwSidetone
                    : row.GetValueOrDefault(BasicOutputDevice.BroadcastMix, hwSidetone);
                effective[BasicOutputDevice.BroadcastMix] = !IsMicCheckActive && broadcastRequested;

                // Dedicated voice comms mic for Discord and games (independent of sidetone)
                effective[BasicOutputDevice.ChatMic] = true;
                effective[BasicOutputDevice.Sampler] = hwSidetone;
            }
            else
            {
                // Hardware feedback loop guards (matching CanRouteToChatMic / CanRouteToSampler)
                // Respects all user routing choices (DMCA Shield IsMusicMutedOnStream, IsChatPrivateInHeadset, Routing Tab, Presets)
                if (input == BasicInputDevice.Chat)
                {
                    effective[BasicOutputDevice.ChatMic] = false;
                }
                if (input == BasicInputDevice.Samples)
                {
                    effective[BasicOutputDevice.Sampler] = false;
                }
            }
        }
        return effective;
    }

    private void PushRoutingForInput(BasicInputDevice input)
    {
        if (!_device.IsConnected) return;
        _device.SetRoutingForInput(input, ComputeEffectiveRoutingForInput(input));
    }

    private void ApplyMicGainToHardware()
    {
        _device.SetMicTypeAndGain(_profile.MicType, _profile.GetActiveMicGainDb());
    }

    private void ApplyNoiseGateToHardware()
    {
        bool on = _profile.GateEnabled && !_voiceCheckBypass;
        int thresh = on ? _profile.GateThresholdDb : -59;
        sbyte attenDb = on ? DspTables.GateAttenuationFromPercent(_profile.GateAttenuationPercent) : (sbyte)0;
        _device.SetMicrophoneFloatParams([
            (MicrophoneParamKey.GateThreshold, thresh),
            (MicrophoneParamKey.GateAttack, _profile.GateAttackIndex),
            (MicrophoneParamKey.GateRelease, _profile.GateReleaseIndex),
            (MicrophoneParamKey.GateAttenuation, attenDb)
        ]);
        _device.SetEffectParams([
            (EffectKey.GateMode, 2),
            (EffectKey.GateEnabled, on ? 1 : 0),
            (EffectKey.GateThreshold, thresh),
            (EffectKey.GateAttack, _profile.GateAttackIndex),
            (EffectKey.GateRelease, _profile.GateReleaseIndex),
            (EffectKey.GateAttenuation, attenDb)
        ]);
    }

    private void ApplyCompressorToHardware()
    {
        bool on = _profile.CompressorEnabled && !_voiceCheckBypass;
        int thresh = on ? _profile.CompressorThresholdDb : 0;
        int ratioIdx = on ? _profile.CompressorRatioIndex : 0; // 1.0:1 when disabled
        int makeUp = on ? EffectiveCompressorMakeUpDb : 0;
        float ratioFloat = DspTables.CompressorRatios[Math.Clamp(ratioIdx, 0, DspTables.CompressorRatios.Length - 1)];

        _device.SetMicrophoneFloatParams([
            (MicrophoneParamKey.CompressorThreshold, thresh),
            (MicrophoneParamKey.CompressorRatio, ratioFloat),
            (MicrophoneParamKey.CompressorAttack, _profile.CompressorAttackIndex),
            (MicrophoneParamKey.CompressorRelease, _profile.CompressorReleaseIndex),
            (MicrophoneParamKey.CompressorMakeUpGain, makeUp)
        ]);
        _device.SetEffectParams([
            (EffectKey.CompressorThreshold, thresh),
            (EffectKey.CompressorRatio, ratioIdx),
            (EffectKey.CompressorAttack, _profile.CompressorAttackIndex),
            (EffectKey.CompressorRelease, _profile.CompressorReleaseIndex),
            (EffectKey.CompressorMakeUpGain, makeUp)
        ]);
    }

    private void ApplyEqToHardware()
    {
        bool on = _profile.EqEnabled;
        _device.SetMicrophoneFloatParams([
            (MicrophoneParamKey.Equalizer90HzFrequency, _profile.Eq90HzFreq),
            (MicrophoneParamKey.Equalizer90HzGain, on ? _profile.Eq90HzGain : 0),
            (MicrophoneParamKey.Equalizer250HzFrequency, _profile.Eq250HzFreq),
            (MicrophoneParamKey.Equalizer250HzGain, on ? _profile.Eq250HzGain : 0),
            (MicrophoneParamKey.Equalizer500HzFrequency, _profile.Eq500HzFreq),
            (MicrophoneParamKey.Equalizer500HzGain, on ? _profile.Eq500HzGain : 0),
            (MicrophoneParamKey.Equalizer1KHzFrequency, _profile.Eq1KHzFreq),
            (MicrophoneParamKey.Equalizer1KHzGain, on ? _profile.Eq1KHzGain : 0),
            (MicrophoneParamKey.Equalizer3KHzFrequency, _profile.Eq3KHzFreq),
            (MicrophoneParamKey.Equalizer3KHzGain, on ? _profile.Eq3KHzGain : 0),
            (MicrophoneParamKey.Equalizer8KHzFrequency, _profile.Eq8KHzFreq),
            (MicrophoneParamKey.Equalizer8KHzGain, on ? _profile.Eq8KHzGain : 0)
        ]);
    }

    private byte[] BuildButtonLedStates()
    {
        byte[] states = new byte[24];
        Array.Fill(states, (byte)0x02); // DimmedColour1 when inactive/unmuted

        int[] faderMuteIdx = [4, 9, 14, 19];
        FaderName[] faders = [FaderName.A, FaderName.B, FaderName.C, FaderName.D];
        for (int i = 0; i < 4; i++)
        {
            var ch = _profile.FaderAssignments.GetValueOrDefault(faders[i], ChannelName.Mic);
            bool muted = _profile.ChannelMuted.GetValueOrDefault(ch, false) || IsChannelCurrentlyDisabled(ch);
            // 0x01 = Bright Color1 when Muted, 0x02 = Dimmed when Unmuted
            states[faderMuteIdx[i]] = muted ? (byte)0x01 : (byte)0x02;
        }

        // Bleep (#!*@) button (index 22)
        states[22] = _isBleepHeld ? (byte)0x01 : (byte)0x02;

        // Cough / Mic Mute button (index 23)
        bool micMuted = _profile.ChannelMuted.GetValueOrDefault(ChannelName.Mic, false) || IsChannelCurrentlyDisabled(ChannelName.Mic);
        states[23] = micMuted ? (byte)0x01 : (byte)0x02;
        return states;
    }

    private void UpdateButtonLeds()
    {
        UpdateAnimationCache();
        if (!_device.IsConnected) return;
        _device.SetButtonStates(BuildButtonLedStates());
    }

    private void AutoSaveIfEnabled()
    {
        if (_settings.AutoSaveProfiles)
        {
            _pendingAutoSave = true;
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }
    }

    private static ChannelName MapInputToChannel(BasicInputDevice input) => input switch
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

    private static bool TryMapChannelToInput(ChannelName ch, out BasicInputDevice input)
    {
        switch (ch)
        {
            case ChannelName.Mic: input = BasicInputDevice.Microphone; return true;
            case ChannelName.Chat: input = BasicInputDevice.Chat; return true;
            case ChannelName.Music: input = BasicInputDevice.Music; return true;
            case ChannelName.Game: input = BasicInputDevice.Game; return true;
            case ChannelName.Console: input = BasicInputDevice.Console; return true;
            case ChannelName.LineIn: input = BasicInputDevice.LineIn; return true;
            case ChannelName.System: input = BasicInputDevice.System; return true;
            case ChannelName.Sample: input = BasicInputDevice.Samples; return true;
            default: input = default; return false;
        }
    }

    private static bool TryMapChannelToOutput(ChannelName ch, out BasicOutputDevice output)
    {
        switch (ch)
        {
            case ChannelName.Headphones: output = BasicOutputDevice.Headphones; return true;
            case ChannelName.LineOut: output = BasicOutputDevice.LineOut; return true;
            default: output = default; return false;
        }
    }

    private static bool TryMapOutputToChannel(BasicOutputDevice output, out ChannelName ch)
    {
        switch (output)
        {
            case BasicOutputDevice.Headphones: ch = ChannelName.Headphones; return true;
            case BasicOutputDevice.LineOut: ch = ChannelName.LineOut; return true;
            default: ch = default; return false;
        }
    }

    private bool _isDisposed = false;

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try { _pollCts.Cancel(); } catch { }
        _autoSaveTimer.Stop();
        if (_pendingAutoSave && _settings.AutoSaveProfiles)
        {
            _pendingAutoSave = false;
            ProfileManager.SaveProfile(_profile);
        }
        _device.Dispose();
        _micMonitor.Dispose();
        _chatMicMeter.Dispose();
        _audioMonitorService.Dispose();
        try { _pollCts.Dispose(); } catch { }

        // When Project XLR closes, restore Windows Default Playback to the user's active DAC / headphones
        // (e.g. AudioQuest DragonFly Red) so their computer sound never cuts out or goes silent!
        string? targetRestoreId = null;
        if (SelectedPlaybackDevice != null && !SelectedPlaybackDevice.IsGoXlrHardwareDirect)
        {
            targetRestoreId = SelectedPlaybackDevice.Id;
        }
        else if (!string.IsNullOrWhiteSpace(_profile?.MonitorOutputDeviceId) && _profile.MonitorOutputDeviceId != AudioMonitorService.HardwareDirectId)
        {
            targetRestoreId = _profile.MonitorOutputDeviceId;
        }

        if (!string.IsNullOrWhiteSpace(targetRestoreId))
        {
            WindowsAudioEndpointManager.RestoreDeviceAsDefault(targetRestoreId);
        }
    }
}
