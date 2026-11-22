using ProjectXLR.Models;
using Xunit;

namespace ProjectXLR.Tests;

public class VoiceCheckAnalyzerTests
{
    private static readonly VoiceCheckSettings Defaults = new(60, true, -42, true, -18, 3.2, 4);

    private static double[] Around(double level, int count = 100, double spread = 3) =>
        Enumerable.Range(0, count).Select(i => level + spread * (i % 5 - 2) / 2.0).ToArray();

    [Fact]
    public void GateBelowQuietVoice_IsLoweredSoQuietSpeechPassesAtAnyLevel()
    {
        var r = VoiceCheckAnalyzer.Analyze(Around(-62), Around(-46), Around(-18), Defaults);

        Assert.True(r.Succeeded);
        Assert.True(r.GateThresholdDb < -46, $"gate {r.GateThresholdDb}");
        Assert.True(r.GateThresholdDb > -62, $"gate {r.GateThresholdDb}");
        Assert.Contains(r.Changes, c => c.Contains("noise gate"));
    }

    [Fact]
    public void GainChange_ShiftsGateByTheSameAmount()
    {
        // Normal voice 10 dB too low: gain rises, and room and voice rise with it.
        var r = VoiceCheckAnalyzer.Analyze(Around(-65), Around(-50), Around(-26), Defaults);

        Assert.True(r.GainDb > Defaults.GainDb);
        double quietAfter = -50 + (r.GainDb - Defaults.GainDb);
        Assert.True(r.GateThresholdDb <= quietAfter - 4, $"gate {r.GateThresholdDb}, quiet {quietAfter}");
    }

    [Fact]
    public void VoiceAlreadyInRange_KeepsGain()
    {
        var r = VoiceCheckAnalyzer.Analyze(Around(-60), Around(-40), Around(-15), Defaults);

        Assert.Equal(Defaults.GainDb, r.GainDb);
    }

    [Fact]
    public void Clipping_LowersGain()
    {
        var r = VoiceCheckAnalyzer.Analyze(Around(-50), Around(-25), Around(-1, spread: 1), Defaults);

        Assert.True(r.GainDb < Defaults.GainDb);
    }

    [Fact]
    public void GainNeverExceedsMaximum_AndWarns()
    {
        var r = VoiceCheckAnalyzer.Analyze(Around(-68), Around(-55), Around(-40), Defaults with { GainDb = 70 });

        Assert.Equal(72, r.GainDb);
        Assert.Contains(r.Warnings, w => w.Contains("maximum"));
    }

    [Fact]
    public void NoiseCloseToQuietVoice_FavoursTheVoiceAndWarns()
    {
        var r = VoiceCheckAnalyzer.Analyze(Around(-45), Around(-40, spread: 1), Around(-15), Defaults);

        Assert.True(r.Succeeded);
        Assert.Contains(r.Warnings, w => w.Contains("room noise"));
        Assert.True(r.GateThresholdDb <= -40 - 4 + 1, $"gate {r.GateThresholdDb}");
    }

    [Fact]
    public void NoSpeechHeard_FailsWithoutChangingAnything()
    {
        var r = VoiceCheckAnalyzer.Analyze(Around(-50), Around(-50), Around(-50), Defaults);

        Assert.False(r.Succeeded);
        Assert.Equal(Defaults.GainDb, r.GainDb);
        Assert.Equal(Defaults.GateThresholdDb, r.GateThresholdDb);
        Assert.Empty(r.Changes);
    }

    [Fact]
    public void NoReadings_Fails()
    {
        var r = VoiceCheckAnalyzer.Analyze([], [], [], Defaults);

        Assert.False(r.Succeeded);
    }

    [Fact]
    public void QuietSpeechNotDetected_WarnsButStillSucceeds()
    {
        var r = VoiceCheckAnalyzer.Analyze(Around(-60), Around(-60), Around(-15), Defaults);

        Assert.True(r.Succeeded);
        Assert.Contains(r.Warnings, w => w.Contains("quiet voice"));
    }

    [Fact]
    public void LevelerOff_IsLeftAlone()
    {
        var off = Defaults with { CompressorEnabled = false };
        var r = VoiceCheckAnalyzer.Analyze(Around(-60), Around(-40), Around(-15), off);

        Assert.Equal(off.CompressorThresholdDb, r.CompressorThresholdDb);
        Assert.Equal(off.CompressorMakeUpDb, r.CompressorMakeUpDb);
    }

    [Fact]
    public void Leveler_GetsTheSameGentleBaseEveryRun()
    {
        var a = VoiceCheckAnalyzer.Analyze(Around(-60), Around(-38), Around(-15), Defaults);
        var b = VoiceCheckAnalyzer.Analyze(Around(-60), Around(-30), Around(-18), Defaults);

        Assert.Equal(a.CompressorThresholdDb, b.CompressorThresholdDb);
        Assert.Equal(a.CompressorMakeUpDb, b.CompressorMakeUpDb);
    }

    [Fact]
    public void SmallDifferenceBetweenRuns_LeavesMicLevelAlone()
    {
        var r = VoiceCheckAnalyzer.Analyze(Around(-60), Around(-40), Around(-19), Defaults);

        Assert.Equal(Defaults.GainDb, r.GainDb);
    }

    [Fact]
    public void QuietKeyboard_GateSitsAboveKeysAndBelowVoice()
    {
        var r = VoiceCheckAnalyzer.Analyze(Around(-60), Around(-30), Around(-15), Defaults, typing: Around(-48));

        Assert.False(r.KeysOverlapVoice);
        Assert.InRange(r.GateThresholdDb, -46, -36);
    }

    [Fact]
    public void KeyboardAsLoudAsQuietVoice_ProtectsVoiceAndWarns()
    {
        var r = VoiceCheckAnalyzer.Analyze(Around(-60), Around(-35), Around(-15), Defaults, typing: Around(-33));

        Assert.True(r.KeysOverlapVoice);
        Assert.Contains(r.Warnings, w => w.Contains("keyboard"));
        Assert.True(r.GateThresholdDb <= -35 - 4, $"gate {r.GateThresholdDb}");
    }

    [Fact]
    public void GainChange_AppliesToKeyboardLevelToo()
    {
        // Normal voice 10 dB low, so everything rises by about 10 dB, including the keys.
        var r = VoiceCheckAnalyzer.Analyze(Around(-65), Around(-40), Around(-26), Defaults, typing: Around(-58));

        double gainChange = r.GainDb - Defaults.GainDb;
        Assert.True(gainChange > 0);
        Assert.True(r.GateThresholdDb >= -56.5 + gainChange, $"gate {r.GateThresholdDb}");
    }

    [Fact]
    public void GateStaysWithinHardwareRange()
    {
        var r = VoiceCheckAnalyzer.Analyze(Around(-72), Around(-60), Around(-15), Defaults);

        Assert.InRange(r.GateThresholdDb, -59, 0);
    }
}
