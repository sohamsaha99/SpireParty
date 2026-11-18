using System;
using System.Text;
using SpireParty.Core;
using SpireParty.Net;
using Xunit;

namespace SpireParty.Tests;

public class EnemyHealthTests
{
    [Theory]
    [InlineData(88.0, 1.0, 8, 88.0)]    // factor 1: the game's value
    [InlineData(88.0, 0.5, 8, 44.0)]
    [InlineData(66.0, 0.15, 5, 10.0)]   // 9.9 rounds up
    [InlineData(66.0, 1.5, 5, 99.0)]
    [InlineData(1.0, 0.5, 2, 1.0)]      // never below 1
    [InlineData(40.0, 0.5, 1, 40.0)]    // single player: untouched, as the game leaves it
    public void ScaleHp(double gameHp, double factor, int players, double expected) =>
        Assert.Equal((decimal)expected, PartyGameplay.ScaleHp((decimal)gameHp, (decimal)factor, players));

    [Fact]
    public void Multiplier_IsClampedTo_HalfToThree()
    {
        Assert.Equal(0.5m, PartyGameplay.ClampEnemyHealth(0.1m));
        Assert.Equal(3.0m, PartyGameplay.ClampEnemyHealth(9m));
        Assert.Equal(1.25m, PartyGameplay.ClampEnemyHealth(1.25m));
    }

    [Fact]
    public void Welcome_CarriesTheMultiplier_AndMissingMeansOne()
    {
        var w = new Welcome(7000002, "ravi", "gate", null, "everything", 2, EnemyHealthMultiplier: 0.75m);
        var back = Assert.IsType<Welcome>(RoundTrip(w));
        Assert.Equal(0.75m, back.EnemyHealthMultiplier);

        var old = ControlCodec.Decode(Encoding.UTF8.GetBytes(
            """{"type":"welcome","playerId":7000002,"seatId":"ravi","party":"gate","unlocks":"everything"}"""));
        Assert.Equal(1m, Assert.IsType<Welcome>(old).EnemyHealthMultiplier);
    }

    static ControlMessage RoundTrip(ControlMessage m)
    {
        byte[] frame = ControlCodec.Encode(m);
        return ControlCodec.Decode(frame.AsSpan(4));
    }
}
