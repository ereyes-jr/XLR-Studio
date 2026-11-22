using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectXLR.Models;

/// <summary>One slice of levels: what apps receive from Chat Mic, plus the GoXLR's own mic meter.</summary>
public readonly record struct VoiceFrame(double TimeSeconds, double ChatRmsDb, double ChatPeakDb, double MicDb);

/// <summary>
/// Slowly adjusts a boost so sustained speech on Chat Mic sits at a steady level. It measures the output
/// it controls, so each step's effect is seen on the next reading. It holds still in silence, and short
/// sounds like key clicks don't count as speech.
/// </summary>
public sealed class VoiceLeveler
{
    public const double TargetDb = -20.0;
    public const double SpeechFloorDb = -48.0;
    public const int MaxBoostDb = 8;
    private const double SustainSeconds = 0.25;
    private const double AverageSeconds = 6.0;
    private const double StepSeconds = 3.0;
    private const double PeakStepSeconds = 1.0;
    private const double MinSpeechBetweenStepsSeconds = 1.5;
    private const double DeadbandDb = 4.0; // Normal speech varies more than this, so smaller errors aren't worth chasing
    private const double PeakLimitDb = -2.0;
    private const double SpeechHoldSeconds = 1.0;

    private double? _speechDb;
    private double _lastTime = double.NaN;
    private double _sustained;
    private double _lastSpeech = double.NegativeInfinity;
    private double _lastStep = double.NegativeInfinity;
    private double _speechSinceStep;

    public int BoostDb { get; private set; }
    public int MinBoostDb { get; set; }

    public void Reset()
    {
        BoostDb = 0;
        _speechDb = null;
        _lastTime = double.NaN;
        _sustained = 0;
        _lastSpeech = double.NegativeInfinity;
        _lastStep = double.NegativeInfinity;
        _speechSinceStep = 0;
    }

    /// <summary>Feeds one reading. Returns true when BoostDb changed.</summary>
    public bool Update(VoiceFrame frame)
    {
        double dt = double.IsNaN(_lastTime) ? 0 : Math.Clamp(frame.TimeSeconds - _lastTime, 0, 0.5);
        _lastTime = frame.TimeSeconds;

        _sustained = frame.ChatRmsDb >= SpeechFloorDb ? _sustained + dt : 0;
        if (_sustained >= SustainSeconds)
        {
            _lastSpeech = frame.TimeSeconds;
            _speechSinceStep += dt;
            double blend = Math.Clamp(dt / AverageSeconds, 0, 1);
            _speechDb = _speechDb is { } level ? level + (frame.ChatRmsDb - level) * blend : frame.ChatRmsDb;
        }

        double sinceStep = frame.TimeSeconds - _lastStep;
        int step = 0;
        if (frame.ChatPeakDb >= PeakLimitDb)
        {
            if (sinceStep < PeakStepSeconds) return false;
            step = -2; // Pull back right away if it's about to distort
        }
        else if (sinceStep >= StepSeconds && _speechSinceStep >= MinSpeechBetweenStepsSeconds &&
                 _speechDb is { } speech && frame.TimeSeconds - _lastSpeech <= SpeechHoldSeconds)
        {
            double error = TargetDb - speech;
            if (error > DeadbandDb) step = 1;
            else if (error < -DeadbandDb) step = -1;
        }

        int next = Math.Clamp(BoostDb + step, MinBoostDb, MaxBoostDb);
        if (next == BoostDb)
        {
            return false;
        }

        // Assume the step lands, so the average doesn't keep asking for the same correction.
        if (_speechDb is { } current) _speechDb = current + (next - BoostDb);
        BoostDb = next;
        _lastStep = frame.TimeSeconds;
        _speechSinceStep = 0;
        return true;
    }
}

public enum CoachHint
{
    None,
    AppsNotReceivingMic,
    TooLoud,
    GateCuttingWords,
    TooQuiet
}

/// <summary>Watches the last few seconds of levels and offers one piece of advice at a time, only for problems that persist.</summary>
public sealed class VoiceCoach
{
    private const double WindowSeconds = 15.0;
    private const double SnoozeSeconds = 300.0;
    private const double ClearSeconds = 60.0;
    private const double MicSpeakingDb = -35.0;
    private const double ChatSilentDb = -75.0;
    private const double ClipDb = -1.0;
    private const double GateBandDb = 6.0;
    private const double QuietMarginDb = 8.0;

    private readonly Queue<(VoiceFrame Frame, double Span)> _frames = new();
    private readonly Dictionary<CoachHint, double> _snoozedUntil = [];
    private double _lastTime = double.NaN;
    private double _lastSeen;

    public CoachHint Current { get; private set; }

    public void Clear()
    {
        _frames.Clear();
        _lastTime = double.NaN;
        Current = CoachHint.None;
    }

    /// <summary>Hides the current advice for a while, and drops the readings that caused it.</summary>
    public void Dismiss(double now)
    {
        if (Current != CoachHint.None) _snoozedUntil[Current] = now + SnoozeSeconds;
        _frames.Clear();
        Current = CoachHint.None;
    }

    /// <summary>Feeds one reading. Returns true when the advice changed.</summary>
    public bool Update(VoiceFrame frame, bool gateEnabled, int gateDb, bool muted, bool boostAtMax)
    {
        double span = double.IsNaN(_lastTime) ? 0 : Math.Clamp(frame.TimeSeconds - _lastTime, 0, 0.5);
        _lastTime = frame.TimeSeconds;
        _frames.Enqueue((frame, span));
        while (_frames.Count > 0 && frame.TimeSeconds - _frames.Peek().Frame.TimeSeconds > WindowSeconds)
        {
            _frames.Dequeue();
        }

        var previous = Current;
        var found = muted ? CoachHint.None : Evaluate(gateEnabled, gateDb, boostAtMax);
        if (found != CoachHint.None && frame.TimeSeconds < _snoozedUntil.GetValueOrDefault(found))
        {
            found = CoachHint.None;
        }

        if (found != CoachHint.None)
        {
            Current = found;
            _lastSeen = frame.TimeSeconds;
        }
        else if (Current != CoachHint.None && frame.TimeSeconds - _lastSeen > ClearSeconds)
        {
            Current = CoachHint.None; // Fixed by itself, so stop mentioning it
        }
        return Current != previous;
    }

    private CoachHint Evaluate(bool gateEnabled, int gateDb, bool boostAtMax)
    {
        double micLoud = 0, chatSpeech = 0, nearGate = 0, aroundVoice = 0, speechSum = 0;
        int clips = 0;
        double loudestChat = double.NegativeInfinity;
        foreach (var (f, span) in _frames)
        {
            loudestChat = Math.Max(loudestChat, f.ChatPeakDb);
            if (f.ChatPeakDb >= ClipDb) clips++;
            if (f.MicDb >= MicSpeakingDb && (!gateEnabled || f.MicDb >= gateDb + 3)) micLoud += span;
            if (f.ChatRmsDb >= VoiceLeveler.SpeechFloorDb)
            {
                chatSpeech += span;
                speechSum += f.ChatRmsDb * span;
            }
            if (gateEnabled && f.MicDb >= gateDb - GateBandDb)
            {
                aroundVoice += span;
                if (f.MicDb < gateDb) nearGate += span;
            }
        }

        if (micLoud >= 4 && loudestChat <= ChatSilentDb) return CoachHint.AppsNotReceivingMic;
        if (clips >= 3) return CoachHint.TooLoud;
        if (chatSpeech >= 3 && aroundVoice > 0 && nearGate / aroundVoice >= 0.25) return CoachHint.GateCuttingWords;
        if (chatSpeech >= 5 && boostAtMax && speechSum / chatSpeech < VoiceLeveler.TargetDb - QuietMarginDb) return CoachHint.TooQuiet;
        return CoachHint.None;
    }
}
