using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectXLR.Models;

public sealed record VoiceCheckSettings(int GainDb, bool GateEnabled, int GateThresholdDb, bool CompressorEnabled, int CompressorThresholdDb, double CompressorRatio, int CompressorMakeUpDb);

public sealed record VoiceCheckResult(
    bool Succeeded,
    string Headline,
    int GainDb,
    int GateThresholdDb,
    int CompressorThresholdDb,
    int CompressorMakeUpDb,
    IReadOnlyList<string> Changes,
    IReadOnlyList<string> Warnings,
    bool KeysOverlapVoice = false);

/// <summary>
/// Turns three short level recordings (room silence, quiet speech, normal speech) into mic settings.
/// All levels are dBFS readings from the GoXLR's mic level meter, taken with the gate and leveler bypassed.
/// </summary>
public static class VoiceCheckAnalyzer
{
    // The meter's Ideal zone runs from -30 to -6 dB; loud moments of normal speech should sit well inside it.
    public const double TargetNormalPeakDb = -14.0;
    private const double ActiveAboveRoomDb = 6.0;
    private const double MinSpeechAboveRoomDb = 10.0;
    private const double MinActiveFraction = 0.10;
    private const double ClipDb = -1.0;
    private const int GainDeadbandDb = 5; // Two readings of the same voice easily differ by a few dB
    private const double MinGateMarginBelowVoiceDb = 4.0;
    private const double MinSeparationDb = 8.0;
    // Live changes belong to the leveler, so this stays a fixed, gentle base instead of being re-measured each run.
    private const int LevelerThresholdDb = -24;
    private const int LevelerMakeUpDb = 3;
    private const double KeysClearanceDb = 2.0;
    private const int MinGateDb = -59;
    private const int MaxGainDb = 72;

    public static VoiceCheckResult Analyze(
        IReadOnlyCollection<double> room,
        IReadOnlyCollection<double> quiet,
        IReadOnlyCollection<double> normal,
        VoiceCheckSettings current,
        IReadOnlyCollection<double>? typing = null)
    {
        if (room.Count == 0 || quiet.Count == 0 || normal.Count == 0)
        {
            return Failed(current, "The check didn't get any readings from your GoXLR.", "Make sure it's connected, then try again.");
        }

        double roomLevel = Percentile(room, 0.90);
        double normalPeak = Percentile(normal, 0.90);
        double activeFloor = roomLevel + ActiveAboveRoomDb;
        var normalActive = normal.Where(v => v >= activeFloor).ToList();
        var quietActive = quiet.Where(v => v >= activeFloor).ToList();

        if (normalPeak < roomLevel + MinSpeechAboveRoomDb || normalActive.Count < normal.Count * MinActiveFraction)
        {
            return Failed(current, "Your voice was hard to tell apart from the room.",
                "Check the mic is plugged in and unmuted, speak close to it, and try again.");
        }

        var changes = new List<string>();
        var warnings = new List<string>();

        // Soft word endings are what get cut off, so the gate is set from the low end of quiet speech.
        bool heardQuiet = quietActive.Count >= quiet.Count * MinActiveFraction;
        var quietReadings = heardQuiet ? quietActive : quiet;
        double quietLow = Percentile(quietReadings, 0.25);
        if (!heardQuiet)
        {
            warnings.Add("Your quiet voice barely rose above the room, so very soft speech may still get lost. Try speaking a little closer to the mic.");
        }

        bool clipped = normal.Count(v => v >= ClipDb) > normal.Count * 0.02;
        int gain = PickGain(current.GainDb, normalPeak, clipped, warnings);
        int gainChange = gain - current.GainDb;
        if (gainChange != 0)
        {
            changes.Add(gainChange > 0
                ? $"Raised Mic level from {current.GainDb} to {gain} dB, so your normal voice reaches Ideal."
                : $"Lowered Mic level from {current.GainDb} to {gain} dB, so your normal voice stays out of Too loud.");
        }

        double roomAfter = roomLevel + gainChange;
        double quietLowAfter = quietLow + gainChange;

        // Key clicks are short, so their loud end is what opens the gate.
        double? keysAfter = typing is { Count: > 0 } ? Percentile(typing, 0.95) + gainChange : null;
        bool keysOverlap = keysAfter is { } k && k > quietLowAfter - MinGateMarginBelowVoiceDb - KeysClearanceDb;
        if (keysOverlap)
        {
            warnings.Add("Your keyboard is about as loud as your quiet voice, so the noise gate can't block it without cutting off your words. AI noise removal has to do that job.");
        }

        int gate = PickGate(roomAfter, quietLowAfter, keysAfter, warnings);
        if (!current.GateEnabled)
        {
            changes.Add($"Turned the noise gate on at {gate} dB, between your room noise and your quiet voice.");
        }
        else if (gate != current.GateThresholdDb)
        {
            string reason = current.GateThresholdDb + gainChange > quietLowAfter
                ? $"your quiet voice was {Math.Round(current.GateThresholdDb + gainChange - quietLowAfter)} dB below it"
                : "that sits between your room noise and your quiet voice";
            changes.Add($"Moved the noise gate from {current.GateThresholdDb} to {gate} dB, because {reason}.");
        }

        var (compThreshold, makeUp) = PickLeveler(current, changes);

        string headline = changes.Count == 0
            ? "Your settings already suit your voice."
            : "Your settings now match your voice.";
        return new VoiceCheckResult(true, headline, gain, gate, compThreshold, makeUp, changes, warnings, keysOverlap);
    }

    private static int PickGain(int currentGain, double normalPeak, bool clipped, List<string> warnings)
    {
        int wanted = currentGain + (int)Math.Round(TargetNormalPeakDb - normalPeak);
        if (!clipped && Math.Abs(wanted - currentGain) < GainDeadbandDb)
        {
            return currentGain;
        }
        if (wanted > MaxGainDb)
        {
            warnings.Add("Your mic is at its maximum level and your voice is still low. Speak closer to the mic, or consider an inline preamp.");
        }
        return Math.Clamp(wanted, 0, MaxGainDb);
    }

    private static int PickGate(double room, double quietLow, double? keys, List<string> warnings)
    {
        double separation = quietLow - room;
        double highestSafe = quietLow - MinGateMarginBelowVoiceDb;
        double gate;
        if (keys is { } k && k > room && separation >= MinSeparationDb)
        {
            // Sit just above the keys if the voice leaves room; otherwise go as high as is safe for the voice.
            gate = Math.Min(k + KeysClearanceDb, highestSafe);
        }
        else if (separation >= MinSeparationDb)
        {
            // Closer to the room than to the voice: letting a little noise through beats cutting off words.
            gate = room + separation * 0.4;
        }
        else
        {
            warnings.Add("Your room noise is close to your quiet voice, so the gate can't fully separate them. I favored keeping your voice. AI noise removal can handle the rest.");
            gate = quietLow - MinGateMarginBelowVoiceDb;
        }
        gate = Math.Min(gate, quietLow - MinGateMarginBelowVoiceDb);
        return Math.Clamp((int)Math.Round(gate), MinGateDb, 0);
    }

    private static (int Threshold, int MakeUp) PickLeveler(VoiceCheckSettings current, List<string> changes)
    {
        if (!current.CompressorEnabled)
        {
            return (current.CompressorThresholdDb, current.CompressorMakeUpDb);
        }

        if (current.CompressorThresholdDb != LevelerThresholdDb || current.CompressorMakeUpDb != LevelerMakeUpDb)
        {
            changes.Add("Set Even out my volume to a gentle starting point. Keep my volume steady handles the rest as you talk.");
        }
        return (LevelerThresholdDb, LevelerMakeUpDb);
    }

    private static VoiceCheckResult Failed(VoiceCheckSettings current, string headline, string warning) =>
        new(false, headline, current.GainDb, current.GateThresholdDb, current.CompressorThresholdDb, current.CompressorMakeUpDb, [], [warning]);

    public static double Percentile(IReadOnlyCollection<double> values, double fraction)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[(int)Math.Clamp(Math.Ceiling(fraction * sorted.Count) - 1, 0, sorted.Count - 1)];
    }
}
