using System;
using System.Text;
using SpireParty.Core;
using SpireParty.Net;
using Xunit;

namespace SpireParty.Tests;

public class PartySettingsTests
{
    [Fact]
    public void Choices_MatchTheDesignDefaults()
    {
        Assert.Equal(new[] { 2, 3, 4, 5, 6, 7, 8 }, PartySettings.MaxPlayerChoices);
        Assert.Equal(new[] { 0, 30, 45, 60, 90 }, PartySettings.TurnTimerChoices);
        var defaults = PartySettings.Of(new PartyFile());
        Assert.Equal(new PartySettings(8, 1.0, 0, 2), defaults);
        Assert.Null(defaults.Check(playersInRun: 0, gameCap: 8));
    }

    [Theory]
    [InlineData(9, 1.0, 0, 2, 0, 8, "Max players must be 2 to 8.")]
    [InlineData(8, 1.0, 0, 2, 0, 4, "at most 4 players")]
    [InlineData(4, 1.0, 0, 2, 5, 8, "already has 5 players")]
    [InlineData(8, 0.25, 0, 2, 0, 8, "Enemy health must be 0.5 to 3.0.")]
    [InlineData(8, 1.0, 40, 2, 0, 8, "Turn timer must be")]
    [InlineData(8, 1.0, 0, 6, 0, 8, "AFK strikes must be 1 to 5.")]
    public void Check_RefusesWithAReason(int max, double health, int timer, int afk, int inRun, int cap, string reason) =>
        Assert.Contains(reason, new PartySettings(max, health, timer, afk).Check(inRun, cap));

    [Fact]
    public void Summary_ReadsWell()
    {
        Assert.Equal("Up to 8 players · enemy health ×1 · turn timer off · AFK after 2 timed-out turns",
            new PartySettings(8, 1.0, 0, 2).Summary());
        Assert.Equal("Up to 5 players · enemy health ×0.75 · turn timer 30 s · AFK after 1 timed-out turn",
            new PartySettings(5, 0.75, 30, 1).Summary());
    }

    [Fact]
    public void Welcome_CarriesTimerAndAfk_MissingKeysGiveDefaults()
    {
        var w = new Welcome(7000002, "ravi", "gate", null, "everything", 2, TurnTimerSeconds: 45, AfkStrikes: 3);
        var back = Assert.IsType<Welcome>(ControlCodec.Decode(ControlCodec.Encode(w).AsSpan(4)));
        Assert.Equal((45, 3), (back.TurnTimerSeconds, back.AfkStrikes));

        var old = Assert.IsType<Welcome>(ControlCodec.Decode(Encoding.UTF8.GetBytes(
            """{"type":"welcome","playerId":7000002,"seatId":"ravi","party":"gate","unlocks":"everything"}""")));
        Assert.Equal((0, 2, 1m), (old.TurnTimerSeconds, old.AfkStrikes, old.EnemyHealthMultiplier));
    }

    [Fact]
    public void TimerPause_AndWelcomePausedFlag_RoundTrip()
    {
        Assert.True(Assert.IsType<TimerPause>(ControlCodec.Decode(ControlCodec.Encode(new TimerPause(true)).AsSpan(4))).Paused);
        Assert.False(Assert.IsType<TimerPause>(ControlCodec.Decode(ControlCodec.Encode(new TimerPause(false)).AsSpan(4))).Paused);
        var w = new Welcome(7000002, "ravi", "gate", null, "everything", 2, TimerPaused: true);
        Assert.True(Assert.IsType<Welcome>(ControlCodec.Decode(ControlCodec.Encode(w).AsSpan(4))).TimerPaused);
    }
}
