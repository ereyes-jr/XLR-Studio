using ProjectXLR.Models;
using Xunit;

namespace ProjectXLR.Tests;

public class VoiceLevelerTests
{
    /// <summary>Simulates a closed loop: the voice's level plus the current boost is what the leveler measures.</summary>
    private static double Run(VoiceLeveler leveler, double voiceDb, double seconds, double start = 0)
    {
        double level = voiceDb;
        for (double t = start; t < start + seconds; t += 0.05)
        {
            level = voiceDb + leveler.BoostDb;
            leveler.Update(new VoiceFrame(t, level, level + 6, level));
        }
        return level;
    }

    [Fact]
    public void QuietVoice_IsLiftedTowardTheTarget()
    {
        var l = new VoiceLeveler();
        double end = Run(l, -30, 60);

        Assert.InRange(end, VoiceLeveler.TargetDb - 5, VoiceLeveler.TargetDb + 5);
        Assert.True(l.BoostDb > 0);
    }

    [Fact]
    public void LoudVoice_IsBroughtDownWithinItsLimit()
    {
        var l = new VoiceLeveler { MinBoostDb = -6 };
        Run(l, -5, 60);

        Assert.Equal(-6, l.BoostDb);
    }

    [Fact]
    public void Silence_HoldsTheBoost()
    {
        var l = new VoiceLeveler();
        Run(l, -30, 10);
        Run(l, -90, 2, start: 10); // A step already due can land during the short hold after speech
        int boost = l.BoostDb;

        Run(l, -90, 30, start: 12);

        Assert.Equal(boost, l.BoostDb);
    }

    [Fact]
    public void ShortClicks_DontCountAsSpeech()
    {
        var l = new VoiceLeveler();
        for (double t = 0; t < 30; t += 0.05)
        {
            bool click = (int)(t * 20) % 10 == 0;
            double level = click ? -35 : -80;
            l.Update(new VoiceFrame(t, level, level, level));
        }

        Assert.Equal(0, l.BoostDb);
    }

    [Fact]
    public void NearDistortion_BacksOffEvenAtTarget()
    {
        var l = new VoiceLeveler { MinBoostDb = -6 };
        for (double t = 0; t < 3; t += 0.05)
        {
            l.Update(new VoiceFrame(t, -20, -1, -20));
        }

        Assert.True(l.BoostDb < 0);
    }

    [Fact]
    public void Boost_NeverExceedsMaximum()
    {
        var l = new VoiceLeveler();
        for (double t = 0; t < 60; t += 0.05)
        {
            l.Update(new VoiceFrame(t, -47, -40, -47)); // Doesn't respond to the boost
        }

        Assert.Equal(VoiceLeveler.MaxBoostDb, l.BoostDb);
    }

    [Fact]
    public void StepsAreGradual()
    {
        var l = new VoiceLeveler();
        int last = 0;
        for (double t = 0; t < 20; t += 0.05)
        {
            l.Update(new VoiceFrame(t, -40, -30, -40));
            Assert.InRange(l.BoostDb - last, -2, 1);
            last = l.BoostDb;
        }
    }

    [Fact]
    public void NormalSpeechSwings_DontMakeItHunt()
    {
        // Syllables swinging 6 dB either side of a voice that's already on target.
        var l = new VoiceLeveler();
        int changes = 0;
        for (double t = 0; t < 60; t += 0.05)
        {
            double level = VoiceLeveler.TargetDb + l.BoostDb + 6 * Math.Sin(t * 8);
            if (l.Update(new VoiceFrame(t, level, level + 6, level))) changes++;
        }

        Assert.Equal(0, changes);
    }

    [Fact]
    public void StepsAreAtLeastThreeSecondsApart()
    {
        var l = new VoiceLeveler();
        double last = double.NegativeInfinity;
        for (double t = 0; t < 30; t += 0.05)
        {
            if (l.Update(new VoiceFrame(t, -45, -35, -45)))
            {
                Assert.True(t - last >= 3.0, $"stepped at {t} after {last}");
                last = t;
            }
        }
    }
}

public class VoiceCoachTests
{
    private static CoachHint Feed(VoiceCoach c, double seconds, double chatRms, double chatPeak, double mic,
        bool gateOn = true, int gate = -50, bool muted = false, bool boostAtMax = false, double start = 0)
    {
        for (double t = start; t < start + seconds; t += 0.05)
        {
            c.Update(new VoiceFrame(t, chatRms, chatPeak, mic), gateOn, gate, muted, boostAtMax);
        }
        return c.Current;
    }

    [Fact]
    public void GoodLevels_GiveNoAdvice() =>
        Assert.Equal(CoachHint.None, Feed(new VoiceCoach(), 20, -20, -12, -20));

    [Fact]
    public void MicActiveButAppsSilent_IsFlagged() =>
        Assert.Equal(CoachHint.AppsNotReceivingMic, Feed(new VoiceCoach(), 6, -100, -100, -20));

    [Fact]
    public void Muted_IsNotFlaggedAsAppsSilent() =>
        Assert.Equal(CoachHint.None, Feed(new VoiceCoach(), 6, -100, -100, -20, muted: true));

    [Fact]
    public void BelowTheGate_IsNotAppsSilent() =>
        Assert.Equal(CoachHint.None, Feed(new VoiceCoach(), 6, -100, -100, -30, gate: -20));

    [Fact]
    public void RepeatedClipping_IsFlagged() =>
        Assert.Equal(CoachHint.TooLoud, Feed(new VoiceCoach(), 2, -10, -0.5, -5));

    [Fact]
    public void QuietWithBoostUsedUp_IsFlagged() =>
        Assert.Equal(CoachHint.TooQuiet, Feed(new VoiceCoach(), 8, -40, -30, -40, boostAtMax: true));

    [Fact]
    public void QuietWithBoostLeft_WaitsForTheLeveler() =>
        Assert.Equal(CoachHint.None, Feed(new VoiceCoach(), 8, -40, -30, -40, boostAtMax: false));

    [Fact]
    public void VoiceHoveringBelowGate_IsFlagged() =>
        Assert.Equal(CoachHint.GateCuttingWords, Feed(new VoiceCoach(), 8, -30, -20, -44, gate: -40));

    [Fact]
    public void Dismiss_HidesTheSameAdviceForAWhile()
    {
        var c = new VoiceCoach();
        Feed(c, 2, -10, -0.5, -5);
        c.Dismiss(2);

        Assert.Equal(CoachHint.None, Feed(c, 10, -10, -0.5, -5, start: 2));
    }

    [Fact]
    public void Advice_ClearsAfterTheProblemStops()
    {
        var c = new VoiceCoach();
        Feed(c, 2, -10, -0.5, -5);

        Assert.Equal(CoachHint.None, Feed(c, 90, -20, -12, -20, start: 2));
    }
}
