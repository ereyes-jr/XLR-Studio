using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ProjectXLR.Hardware;
using ProjectXLR.Models;

namespace ProjectXLR.ViewModels;

public enum VoiceCheckStage
{
    Closed,
    Intro,
    Room,
    Keys,
    Quiet,
    Normal,
    Result
}

/// <summary>
/// The guided "Check my voice" flow: records room, quiet and normal levels with the gate and leveler
/// bypassed on the hardware (the saved profile isn't touched), applies the result, and can undo it.
/// </summary>
public sealed partial class MainViewModel
{
    private const double VoiceCheckGetReadySeconds = 2.0;
    private const double VoiceCheckRecordSeconds = 5.0;

    private sealed record VoiceCheckSnapshot(
        int GainDb,
        bool GateEnabled, int GateThresholdDb, int GateReleaseIndex, int GateAttenuationPercent, int? VoiceCheckGateDb,
        int CompressorThresholdDb, int CompressorMakeUpDb,
        bool EqEnabled, int Eq90, int Eq250, int Eq500, int Eq1K, int Eq3K, int Eq8K,
        string DenoiseStrength);

    private readonly List<double> _voiceCheckRoom = [];
    private readonly List<double> _voiceCheckKeys = [];
    private readonly List<double> _voiceCheckQuiet = [];
    private readonly List<double> _voiceCheckNormal = [];
    private DispatcherTimer? _voiceCheckTimer;
    private DateTime _voiceCheckStageStart;
    private bool _voiceCheckBypass;
    private bool _voiceCheckCollecting;
    private VoiceCheckSnapshot? _voiceCheckSnapshot;
    private VoiceCheckStage _voiceCheckStage;
    private double _voiceCheckProgress;
    private string _voiceCheckHeadline = string.Empty;
    private IReadOnlyList<string> _voiceCheckChanges = [];
    private IReadOnlyList<string> _voiceCheckWarnings = [];
    private bool _voiceCheckSucceeded;

    public VoiceCheckStage VoiceCheckStage
    {
        get => _voiceCheckStage;
        private set
        {
            if (SetProperty(ref _voiceCheckStage, value))
            {
                OnPropertyChanged(nameof(IsVoiceCheckOpen));
                OnPropertyChanged(nameof(IsVoiceCheckIntro));
                OnPropertyChanged(nameof(IsVoiceCheckRecording));
                OnPropertyChanged(nameof(IsVoiceCheckResult));
                OnPropertyChanged(nameof(VoiceCheckTitle));
                OnPropertyChanged(nameof(VoiceCheckInstruction));
                OnPropertyChanged(nameof(VoiceCheckReadAloud));
                OnPropertyChanged(nameof(HasVoiceCheckReadAloud));
                OnPropertyChanged(nameof(VoiceCheckStepText));
            }
        }
    }

    public bool IsVoiceCheckOpen => _voiceCheckStage != VoiceCheckStage.Closed;
    public bool IsVoiceCheckIntro => _voiceCheckStage == VoiceCheckStage.Intro;
    public bool IsVoiceCheckRecording => _voiceCheckStage is VoiceCheckStage.Room or VoiceCheckStage.Keys or VoiceCheckStage.Quiet or VoiceCheckStage.Normal;
    public bool IsVoiceCheckResult => _voiceCheckStage == VoiceCheckStage.Result;

    public string VoiceCheckStepText => _voiceCheckStage switch
    {
        VoiceCheckStage.Room => "Step 1 of 4",
        VoiceCheckStage.Keys => "Step 2 of 4",
        VoiceCheckStage.Quiet => "Step 3 of 4",
        VoiceCheckStage.Normal => "Step 4 of 4",
        _ => string.Empty
    };

    public string VoiceCheckTitle => _voiceCheckStage switch
    {
        VoiceCheckStage.Intro => "Check my voice",
        VoiceCheckStage.Room => "Stay quiet",
        VoiceCheckStage.Keys => "Now type",
        VoiceCheckStage.Quiet => "Now speak softly",
        VoiceCheckStage.Normal => "Now speak normally",
        VoiceCheckStage.Result => _voiceCheckHeadline,
        _ => string.Empty
    };

    public string VoiceCheckInstruction => _voiceCheckStage switch
    {
        VoiceCheckStage.Intro => "This takes about 30 seconds. XLR Studio listens to your room and your voice, then sets your mic to suit you. You can undo it afterwards.",
        VoiceCheckStage.Room => "Don't type or talk. This measures your room's background noise.",
        VoiceCheckStage.Keys => "Type the way you do in a game, without talking. This measures how loud your keyboard is.",
        VoiceCheckStage.Quiet => "Read this as quietly as you ever talk, as if someone nearby is asleep:",
        VoiceCheckStage.Normal => "Read this the way you talk in a game or call:",
        _ => string.Empty
    };

    public string VoiceCheckReadAloud => _voiceCheckStage switch
    {
        VoiceCheckStage.Quiet => "\"I'm keeping my voice down so I don't wake anyone up. Can you still hear every word I'm saying?\"",
        VoiceCheckStage.Normal => "\"Enemy on the left side, I'm pushing up now. Watch out behind you, and tell me when you're ready.\"",
        _ => string.Empty
    };

    public bool HasVoiceCheckReadAloud => !string.IsNullOrEmpty(VoiceCheckReadAloud);

    public double VoiceCheckProgress
    {
        get => _voiceCheckProgress;
        private set => SetProperty(ref _voiceCheckProgress, value);
    }

    public bool VoiceCheckSucceeded
    {
        get => _voiceCheckSucceeded;
        private set
        {
            if (SetProperty(ref _voiceCheckSucceeded, value))
            {
                OnPropertyChanged(nameof(VoiceCheckFailed));
            }
        }
    }

    public bool VoiceCheckFailed => !_voiceCheckSucceeded;

    public IReadOnlyList<string> VoiceCheckChanges
    {
        get => _voiceCheckChanges;
        private set
        {
            if (SetProperty(ref _voiceCheckChanges, value))
            {
                OnPropertyChanged(nameof(HasVoiceCheckChanges));
            }
        }
    }

    public bool HasVoiceCheckChanges => _voiceCheckChanges.Count > 0;

    public IReadOnlyList<string> VoiceCheckWarnings
    {
        get => _voiceCheckWarnings;
        private set
        {
            if (SetProperty(ref _voiceCheckWarnings, value))
            {
                OnPropertyChanged(nameof(HasVoiceCheckWarnings));
            }
        }
    }

    public bool HasVoiceCheckWarnings => _voiceCheckWarnings.Count > 0;

    /// <summary>A strong treble cut muffles soft speech, and a level meter can't detect that, so it's raised by name.</summary>
    public bool IsToneMuffled => _profile.EqEnabled && (_profile.Eq3KHzGain <= -6 || _profile.Eq8KHzGain <= -6);

    public bool IsAiNoiseRemovalOn => DeepFilterNetToggleEnabled;
    public bool CanOfferGentleAi => DeepFilterNetToggleEnabled && !IsDenoiseGentle;
    public bool CanOfferStrongestAi => DeepFilterNetToggleEnabled && !IsDenoiseAggressive;

    public void SetAiStrengthFromVoiceCheck(string level)
    {
        DeepFilterNetDenoiseStrength = level;
        NotifyVoiceCheckAdvice();
    }

    public void OpenVoiceCheck() => VoiceCheckStage = VoiceCheckStage.Intro;

    public void StartVoiceCheck()
    {
        if (!_device.IsConnected)
        {
            StatusBannerMessage = "Connect your GoXLR to check your voice.";
            return;
        }
        if (IsMicMuted)
        {
            StatusBannerMessage = "Your mic is muted. Unmute it, then start the check.";
            return;
        }

        ResetLeveler();
        _voiceCheckRoom.Clear();
        _voiceCheckKeys.Clear();
        _voiceCheckQuiet.Clear();
        _voiceCheckNormal.Clear();
        SetVoiceCheckBypass(true);
        BeginVoiceCheckStage(VoiceCheckStage.Room);

        _voiceCheckTimer ??= CreateVoiceCheckTimer();
        _voiceCheckTimer.Start();
    }

    private DispatcherTimer CreateVoiceCheckTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _uiDispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => VoiceCheckTick();
        return timer;
    }

    private void BeginVoiceCheckStage(VoiceCheckStage stage)
    {
        VoiceCheckStage = stage;
        VoiceCheckProgress = 0;
        _voiceCheckStageStart = DateTime.UtcNow;
        _voiceCheckCollecting = false;
    }

    private void VoiceCheckTick()
    {
        double elapsed = (DateTime.UtcNow - _voiceCheckStageStart).TotalSeconds;
        _voiceCheckCollecting = elapsed >= VoiceCheckGetReadySeconds;
        VoiceCheckProgress = Math.Clamp((elapsed - VoiceCheckGetReadySeconds) / VoiceCheckRecordSeconds * 100.0, 0, 100);

        if (elapsed < VoiceCheckGetReadySeconds + VoiceCheckRecordSeconds)
        {
            return;
        }

        switch (_voiceCheckStage)
        {
            case VoiceCheckStage.Room:
                BeginVoiceCheckStage(VoiceCheckStage.Keys);
                break;
            case VoiceCheckStage.Keys:
                BeginVoiceCheckStage(VoiceCheckStage.Quiet);
                break;
            case VoiceCheckStage.Quiet:
                BeginVoiceCheckStage(VoiceCheckStage.Normal);
                break;
            default:
                FinishVoiceCheck();
                break;
        }
    }

    /// <summary>Called on every hardware poll with the current mic level.</summary>
    private void RecordVoiceCheckSample(double instantDb)
    {
        if (!_voiceCheckCollecting) return;

        switch (_voiceCheckStage)
        {
            case VoiceCheckStage.Room:
                _voiceCheckRoom.Add(instantDb);
                break;
            case VoiceCheckStage.Keys:
                _voiceCheckKeys.Add(instantDb);
                break;
            case VoiceCheckStage.Quiet:
                _voiceCheckQuiet.Add(instantDb);
                break;
            case VoiceCheckStage.Normal:
                _voiceCheckNormal.Add(instantDb);
                break;
        }
    }

    private void FinishVoiceCheck()
    {
        StopVoiceCheckRecording();
        ShowVoiceCheckResult(_voiceCheckRoom, _voiceCheckKeys, _voiceCheckQuiet, _voiceCheckNormal);
    }

    private void ShowVoiceCheckResult(IReadOnlyCollection<double> room, IReadOnlyCollection<double> keys, IReadOnlyCollection<double> quiet, IReadOnlyCollection<double> normal)
    {
        var current = new VoiceCheckSettings(
            MicGainDb,
            _profile.GateEnabled,
            _profile.GateThresholdDb,
            _profile.CompressorEnabled,
            _profile.CompressorThresholdDb,
            DspTables.CompressorRatios[Math.Clamp(_profile.CompressorRatioIndex, 0, DspTables.CompressorRatios.Length - 1)],
            _profile.CompressorMakeUpGainDb);

        var result = VoiceCheckAnalyzer.Analyze(room, quiet, normal, current, keys);
        _voiceCheckHeadline = result.Headline;
        VoiceCheckSucceeded = result.Succeeded;
        VoiceCheckChanges = result.Changes;
        VoiceCheckWarnings = result.Warnings;

        if (result.Succeeded)
        {
            _voiceCheckSnapshot = TakeVoiceCheckSnapshot();
            ApplyVoiceCheckResult(result);
        }

        NotifyVoiceCheckAdvice();
        VoiceCheckStage = VoiceCheckStage.Result;
    }

    private void NotifyVoiceCheckAdvice()
    {
        OnPropertyChanged(nameof(IsToneMuffled));
        OnPropertyChanged(nameof(IsAiNoiseRemovalOn));
        OnPropertyChanged(nameof(CanOfferGentleAi));
        OnPropertyChanged(nameof(CanOfferStrongestAi));
        OnPropertyChanged(nameof(IsAiDoubleSuppressionConflict));
        OnPropertyChanged(nameof(CanUndoVoiceCheck));
    }

    private void ApplyVoiceCheckResult(VoiceCheckResult result)
    {
        MicGainDb = result.GainDb;
        _profile.GateEnabled = true;
        _profile.GateThresholdDb = result.GateThresholdDb;
        _profile.VoiceCheckGateDb = result.GateThresholdDb;
        // 250 ms keeps the ends of soft words. Turning noise down instead of fully off stops the mic snapping on and off.
        _profile.GateReleaseIndex = 20;
        _profile.GateAttenuationPercent = 80;
        _profile.CompressorThresholdDb = result.CompressorThresholdDb;
        _profile.CompressorMakeUpGainDb = result.CompressorMakeUpDb;
        ApplyNoiseGateToHardware();
        ApplyCompressorToHardware();
        SyncViewModelsFromProfile();
        AutoSaveIfEnabled();
        StatusBannerMessage = "Voice check finished.";
    }

    private VoiceCheckSnapshot TakeVoiceCheckSnapshot() => new(
        MicGainDb,
        _profile.GateEnabled, _profile.GateThresholdDb, _profile.GateReleaseIndex, _profile.GateAttenuationPercent, _profile.VoiceCheckGateDb,
        _profile.CompressorThresholdDb, _profile.CompressorMakeUpGainDb,
        _profile.EqEnabled, _profile.Eq90HzGain, _profile.Eq250HzGain, _profile.Eq500HzGain, _profile.Eq1KHzGain, _profile.Eq3KHzGain, _profile.Eq8KHzGain,
        DeepFilterNetDenoiseStrength);


    public bool CanUndoVoiceCheck => _voiceCheckSnapshot != null;

    public void UndoVoiceCheck()
    {
        if (_voiceCheckSnapshot is not { } s) return;

        MicGainDb = s.GainDb;
        DeepFilterNetDenoiseStrength = s.DenoiseStrength;
        _profile.GateEnabled = s.GateEnabled;
        _profile.GateThresholdDb = s.GateThresholdDb;
        _profile.GateReleaseIndex = s.GateReleaseIndex;
        _profile.GateAttenuationPercent = s.GateAttenuationPercent;
        _profile.VoiceCheckGateDb = s.VoiceCheckGateDb;
        _profile.CompressorThresholdDb = s.CompressorThresholdDb;
        _profile.CompressorMakeUpGainDb = s.CompressorMakeUpDb;
        _profile.EqEnabled = s.EqEnabled;
        _profile.Eq90HzGain = s.Eq90;
        _profile.Eq250HzGain = s.Eq250;
        _profile.Eq500HzGain = s.Eq500;
        _profile.Eq1KHzGain = s.Eq1K;
        _profile.Eq3KHzGain = s.Eq3K;
        _profile.Eq8KHzGain = s.Eq8K;
        ApplyNoiseGateToHardware();
        ApplyCompressorToHardware();
        ApplyEqToHardware();
        SyncViewModelsFromProfile();
        AutoSaveIfEnabled();
        StatusBannerMessage = "Voice check undone. Your previous settings are back.";
        NotifyVoiceCheckAdvice();
        CloseVoiceCheck();
    }

    /// <summary>Clears a muffling treble cut found by the check; Undo restores it along with everything else.</summary>
    public void SetToneNaturalFromVoiceCheck()
    {
        SetEqPreset("Flat");
        OnPropertyChanged(nameof(IsToneMuffled));
    }

    /// <summary>Closes the sheet. If it's still recording, stops and puts the gate and leveler back.</summary>
    public void CloseVoiceCheck()
    {
        StopVoiceCheckRecording();
        if (IsMicCheckActive) IsMicCheckActive = false;
        _voiceCheckSnapshot = null;
        OnPropertyChanged(nameof(CanUndoVoiceCheck));
        VoiceCheckStage = VoiceCheckStage.Closed;
    }

    private void StopVoiceCheckRecording()
    {
        _voiceCheckTimer?.Stop();
        _voiceCheckCollecting = false;
        SetVoiceCheckBypass(false);
    }

    private void SetVoiceCheckBypass(bool bypass)
    {
        if (_voiceCheckBypass == bypass) return;
        _voiceCheckBypass = bypass;
        ApplyNoiseGateToHardware();
        ApplyCompressorToHardware();
    }

    /// <summary>Preview-only: shows the result sheet with sample readings so it can be rendered without hardware.</summary>
    public void ShowVoiceCheckPreview()
    {
        if (!PreviewMode) return;
        double[] Around(double level) => Enumerable.Range(0, 100).Select(i => level + 1.5 * (i % 5 - 2)).ToArray();
        ShowVoiceCheckResult(Around(-58), Around(-40), Around(-44), Around(-22));
    }

    public void ShowVoiceCheckStagePreview(VoiceCheckStage stage)
    {
        if (!PreviewMode) return;
        VoiceCheckStage = stage;
        VoiceCheckProgress = 45;
    }
}
