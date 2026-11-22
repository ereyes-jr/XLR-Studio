using System;
using ProjectXLR.Hardware;
using ProjectXLR.Models;

namespace ProjectXLR.ViewModels;

/// <summary>
/// The live voice helpers. The leveler turns the GoXLR's make-up gain up and down at runtime (never saved to
/// the profile), and the coach watches the same readings for problems that persist.
/// </summary>
public sealed partial class MainViewModel
{
    private const double MeterRetrySeconds = 5.0;
    private const double FaderSettleSeconds = 1.5;

    private readonly ChatMicMeter _chatMicMeter = new();
    private readonly VoiceLeveler _leveler = new();
    private readonly VoiceCoach _coach = new();
    private readonly System.Diagnostics.Stopwatch _assistClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly byte[] _assistFaderValues = new byte[4];
    private double _nextMeterAttempt;
    private double _lastFaderMove = double.NegativeInfinity;
    private int _levelerBoostDb;

    public bool VoiceLevelerEnabled
    {
        get => _profile.VoiceLevelerEnabled;
        set
        {
            if (_profile.VoiceLevelerEnabled != value)
            {
                _profile.VoiceLevelerEnabled = value;
                if (!value) SetLevelerBoost(0);
                _leveler.Reset();
                OnPropertyChanged();
                OnPropertyChanged(nameof(VoiceLevelerStatusText));
                AutoSaveIfEnabled();
            }
        }
    }

    public bool VoiceCoachEnabled
    {
        get => _profile.VoiceCoachEnabled;
        set
        {
            if (_profile.VoiceCoachEnabled != value)
            {
                _profile.VoiceCoachEnabled = value;
                _coach.Clear();
                NotifyCoach();
                OnPropertyChanged();
                AutoSaveIfEnabled();
            }
        }
    }

    public string VoiceLevelerStatusText
    {
        get
        {
            if (!_profile.VoiceLevelerEnabled) return "Lifts your voice when you get quieter or lean away.";
            if (!_profile.CompressorEnabled) return "Needs Even out my volume turned on.";
            return _levelerBoostDb switch
            {
                > 0 => $"On. Lifting your voice by {_levelerBoostDb} dB right now.",
                < 0 => $"On. Lowering your voice by {-_levelerBoostDb} dB right now.",
                _ => "On. Your voice is at a good level."
            };
        }
    }

    public bool HasCoachHint => _coach.Current != CoachHint.None;

    public string CoachTitle => _coach.Current switch
    {
        CoachHint.AppsNotReceivingMic => "Your apps aren't receiving your mic",
        CoachHint.TooLoud => "Your voice is distorting",
        CoachHint.GateCuttingWords => "The noise gate is cutting off your words",
        CoachHint.TooQuiet => "You're too far from the mic",
        _ => string.Empty
    };

    public string CoachMessage => _coach.Current switch
    {
        CoachHint.AppsNotReceivingMic => "Your mic is picking you up, but Chat Mic is silent, so Discord and games won't hear you.",
        CoachHint.TooLoud => "You're hitting the top of the meter. Lowering your Mic level will fix it.",
        CoachHint.GateCuttingWords => "Your softer speech keeps landing just under the gate. Lowering it lets those words through.",
        CoachHint.TooQuiet => "The automatic lift is at its limit and you're still quiet. Moving closer helps most, and also keeps your keyboard down.",
        _ => string.Empty
    };

    public string CoachFixLabel => _coach.Current switch
    {
        CoachHint.AppsNotReceivingMic => "Reconnect mic",
        CoachHint.TooLoud => "Lower Mic level",
        CoachHint.GateCuttingWords => "Lower the gate",
        CoachHint.TooQuiet => "Raise Mic level",
        _ => string.Empty
    };

    public void ApplyCoachFix()
    {
        switch (_coach.Current)
        {
            case CoachHint.AppsNotReceivingMic:
                if (!_profile.Routing.TryGetValue(BasicInputDevice.Microphone, out var routes))
                {
                    routes = [];
                    _profile.Routing[BasicInputDevice.Microphone] = routes;
                }
                routes[BasicOutputDevice.ChatMic] = true;
                _profile.DisabledInputs.Remove(BasicInputDevice.Microphone);
                PushRoutingForInput(BasicInputDevice.Microphone);
                AutoSaveIfEnabled();
                StatusBannerMessage = "Reconnected your mic to Chat Mic.";
                break;
            case CoachHint.TooLoud:
                AdjustMicGain(-4);
                StatusBannerMessage = $"Mic level lowered to {MicGainDb} dB.";
                break;
            case CoachHint.GateCuttingWords:
                GateThresholdDb = Math.Max(-59, GateThresholdDb - 6);
                StatusBannerMessage = $"Noise gate lowered to {GateThresholdDb} dB.";
                break;
            case CoachHint.TooQuiet:
                AdjustMicGain(4);
                StatusBannerMessage = $"Mic level raised to {MicGainDb} dB.";
                break;
        }
        DismissCoachHint();
    }

    public void DismissCoachHint()
    {
        _coach.Dismiss(_assistClock.Elapsed.TotalSeconds);
        NotifyCoach();
    }

    private void NotifyCoach()
    {
        OnPropertyChanged(nameof(HasCoachHint));
        OnPropertyChanged(nameof(CoachTitle));
        OnPropertyChanged(nameof(CoachMessage));
        OnPropertyChanged(nameof(CoachFixLabel));
    }

    /// <summary>Called on every hardware poll.</summary>
    private void UpdateVoiceAssist(HardwareButtonAndFaderState? hwState, double micDb)
    {
        double now = _assistClock.Elapsed.TotalSeconds;
        NoteFaderMovement(hwState, now);

        if (IsVoiceCheckOpen || (!_profile.VoiceLevelerEnabled && !_profile.VoiceCoachEnabled))
        {
            return;
        }

        if (!_chatMicMeter.IsRunning)
        {
            if (now >= _nextMeterAttempt)
            {
                _nextMeterAttempt = now + MeterRetrySeconds;
                _chatMicMeter.Start();
            }
            return;
        }

        var (rmsDb, peakDb) = _chatMicMeter.TakeLevels();
        var frame = new VoiceFrame(now, rmsDb, peakDb, micDb);

        if (_profile.VoiceLevelerEnabled && _profile.CompressorEnabled)
        {
            _leveler.MinBoostDb = -_profile.CompressorMakeUpGainDb;
            // Hardware writes while a fader is moving can stall its readings, so wait for it to settle.
            if (now - _lastFaderMove >= FaderSettleSeconds && _leveler.Update(frame))
            {
                SetLevelerBoost(_leveler.BoostDb);
            }
        }

        if (_profile.VoiceCoachEnabled)
        {
            bool boostAtMax = _profile.VoiceLevelerEnabled ? _levelerBoostDb >= VoiceLeveler.MaxBoostDb : true;
            if (_coach.Update(frame, _profile.GateEnabled, _profile.GateThresholdDb, IsMicMuted, boostAtMax))
            {
                NotifyCoach();
            }
        }
    }

    private void NoteFaderMovement(HardwareButtonAndFaderState? hwState, double now)
    {
        if (hwState == null) return;

        int i = 0;
        foreach (var fader in Enum.GetValues<FaderName>())
        {
            byte value = hwState.GetFaderVolume(fader);
            if (Math.Abs(value - _assistFaderValues[i]) > 2)
            {
                _lastFaderMove = now;
            }
            _assistFaderValues[i++] = value;
        }
    }

    private void SetLevelerBoost(int boostDb)
    {
        if (_levelerBoostDb == boostDb) return;

        _levelerBoostDb = boostDb;
        int makeUp = EffectiveCompressorMakeUpDb;
        _device.SetMicrophoneFloatParams([(MicrophoneParamKey.CompressorMakeUpGain, makeUp)]);
        _device.SetEffectParams([(EffectKey.CompressorMakeUpGain, makeUp)]);
        OnPropertyChanged(nameof(VoiceLevelerStatusText));
    }

    private int EffectiveCompressorMakeUpDb => Math.Clamp(_profile.CompressorMakeUpGainDb + _levelerBoostDb, 0, 24);

    /// <summary>Clears any live lift, so the voice check measures the plain mic.</summary>
    private void ResetLeveler()
    {
        _leveler.Reset();
        _coach.Clear();
        NotifyCoach();
        SetLevelerBoost(0);
    }

    public void ShowCoachPreview()
    {
        if (!PreviewMode) return;
        for (double t = 0; t < 2; t += 0.05)
        {
            _coach.Update(new VoiceFrame(t, -30, -20, -44), true, -40, false, false);
        }
        for (double t = 2; t < 6; t += 0.05)
        {
            _coach.Update(new VoiceFrame(t, -30, -20, -44), true, -40, false, false);
        }
        NotifyCoach();
    }
}
