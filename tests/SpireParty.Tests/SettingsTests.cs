using SpireParty.Core;

namespace SpireParty.Tests;

public class SettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void Config_MissingOrEmpty_GivesDefaults(string? json)
    {
        var c = MachineConfig.Parse(json, out var warnings);
        Assert.Equal(33771, c.GamePort);
        Assert.Equal(8771, c.WebPort);
        Assert.Equal("auto", c.ListenAddress);
        Assert.True(c.BrowserSeats);
        Assert.Equal(20, c.BackupsKept);
        Assert.Empty(c.RecentHosts);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Config_BrokenJson_GivesDefaultsWithWarning()
    {
        var c = MachineConfig.Parse("{ gamePort: ", out var warnings);
        Assert.Equal(33771, c.GamePort);
        Assert.Single(warnings);
    }

    [Fact]
    public void Config_BadValuesFallBack_UnknownKeysIgnored_CommentsAndTrailingCommasOk()
    {
        var c = MachineConfig.Parse("""
            {
              // hand-edited
              "gamePort": 70000,
              "webPort": "8080",
              "backupsKept": 50,
              "someFutureKey": [1, 2, 3],
            }
            """, out var warnings);
        Assert.Equal(33771, c.GamePort);
        Assert.Equal(8771, c.WebPort);
        Assert.Equal(50, c.BackupsKept);
        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void Config_RoundTrips_AndRememberKeepsMostRecentFirst()
    {
        var c = MachineConfig.Parse(null, out _);
        c.Remember(new RecentHost("100.64.0.1", 33771, "ravi"));
        c.Remember(new RecentHost("100.64.0.2", 33771, "ravi"));
        c.Remember(new RecentHost("100.64.0.1", 33771, "ravi"));
        Assert.Equal("100.64.0.1", c.RecentHosts[0].Address);
        Assert.Equal(2, c.RecentHosts.Count);

        var again = MachineConfig.Parse(c.ToJson(), out var warnings);
        Assert.Empty(warnings);
        Assert.Equal(c.RecentHosts, again.RecentHosts);
    }

    [Fact]
    public void Party_MissingFile_GivesDesignDefaults()
    {
        var p = PartyFile.Parse(null, out var warnings);
        Assert.Equal(8, p.MaxPlayers);
        Assert.Equal("everything", p.Unlocks);
        Assert.Equal(1.0, p.EnemyHealthMultiplier);
        Assert.Equal(0, p.TurnTimerSeconds);
        Assert.Equal(2, p.AfkStrikes);
        Assert.Equal("highest", p.CatchUpGold);
        Assert.Equal("matchLowest", p.CatchUpMaxHp);
        Assert.Equal(0, p.CatchUpCardRewards);
        Assert.Equal(SeatRules.FirstGuestPlayerId, p.NextPlayerId);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Party_OutOfRangeAndWrongTypes_FallBackPerKey()
    {
        var p = PartyFile.Parse("""
            { "maxPlayers": 12, "enemyHealthMultiplier": 1.5, "turnTimerSeconds": 40,
              "voteToKick": "UNANIMOUS", "catchUp": { "cardRewards": 3, "gold": "lots" } }
            """, out var warnings);
        Assert.Equal(8, p.MaxPlayers);
        Assert.Equal(1.5, p.EnemyHealthMultiplier);
        Assert.Equal(0, p.TurnTimerSeconds);
        Assert.Equal(3, p.CatchUpCardRewards);
        Assert.Equal("highest", p.CatchUpGold);
        Assert.Equal(3, warnings.Count);
    }

    [Theory]
    [InlineData("highest", "highest")]
    [InlineData("Average", "average")]
    [InlineData("lowest", "lowest")]
    [InlineData("starter", "starter")]
    [InlineData("matchLowest", "lowest")]   // M1 name
    [InlineData("starterOnly", "starter")]  // M1 name
    public void Party_CatchUpGold_AcceptsEveryChoiceAndM1Names(string stored, string expected)
    {
        var p = PartyFile.Parse($$"""{ "catchUp": { "gold": "{{stored}}" } }""", out var warnings);
        Assert.Equal(expected, p.CatchUpGold);
        Assert.Empty(warnings);
        Assert.Equal(expected, PartyFile.Parse(p.ToJson(), out _).CatchUpGold);
    }

    [Fact]
    public void Party_BadAndDuplicateSeatsSkipped_NextIdNeverBelowUsedIds()
    {
        var p = PartyFile.Parse("""
            { "nextPlayerId": 7000002,
              "seats": [
                { "seatId": "Ravi", "playerId": 7000005, "state": "benched" },
                { "seatId": "ravi", "playerId": 7000009 },
                { "seatId": "9lives", "playerId": 7000010 },
                "not a seat",
                { "seatId": "mia" }
              ] }
            """, out var warnings);
        var seat = Assert.Single(p.Seats);
        Assert.Equal("ravi", seat.SeatId);
        Assert.Equal(SeatState.Benched, seat.State);
        Assert.Equal(7000006UL, p.NextPlayerId);
        Assert.Equal(4, warnings.Count);
    }

    [Fact]
    public void Party_RoundTrips()
    {
        var p = PartyFile.Parse(null, out _);
        p.Name = "Thursday Crew";
        p.Seats.Add(new Seat { SeatId = "host", PlayerId = 1, DisplayName = "Host" });
        p.Seats.Add(new Seat { SeatId = "ravi", PlayerId = SeatRules.AllocatePlayerId(p), TokenHash = "sha256$a$b", Kind = SeatKind.Browser });

        var again = PartyFile.Parse(p.ToJson(), out var warnings);
        Assert.Empty(warnings);
        Assert.Equal(p.ToJson(), again.ToJson());
    }
}
