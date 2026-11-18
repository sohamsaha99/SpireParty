using System.Text;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using SpireParty.Core;

namespace SpireParty.Tests;

/// <summary>The fail-closed scan on the fixture saves (see <see cref="RosterEditTests"/>).</summary>
public class SaveScanTests
{
    const ulong Tester = 7_000_002, Newbie = 7_000_003;

    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    static PartyFile ThreeParty()
    {
        var p = new PartyFile { Name = "test", NextPlayerId = Newbie + 1 };
        p.Seats.Add(new Seat { SeatId = "host", PlayerId = 1 });
        p.Seats.Add(new Seat { SeatId = "tester", PlayerId = Tester });
        p.Seats.Add(new Seat { SeatId = "newbie", PlayerId = Newbie });
        return p;
    }

    static byte[] Inject(byte[] save, Action<JsonObject> change)
    {
        var root = JsonNode.Parse(save)!.AsObject();
        change(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    [Theory]
    [InlineData(Tester)]
    [InlineData(Newbie)]
    public void CleanSave_HasOnlyKnownReferences(ulong id)
    {
        byte[] save = Fixture("three-player.save");
        var all = SaveScan.FindReferences(save, id);
        int floors = PartyRunSave.FromBytes(save).MapPointHistory.Sum(a => a.Count);

        Assert.Single(all, p => p.EndsWith(".net_id"));
        Assert.Equal(floors, all.Count(p => p.EndsWith(".player_id")));
        Assert.Equal(1 + floors, all.Count);
        Assert.Empty(SaveScan.FindUnknownReferences(save, id));
    }

    [Fact]
    public void UnusedId_HasNoReferences()
    {
        Assert.Empty(SaveScan.FindReferences(Fixture("three-player.save"), Newbie + 1));
    }

    [Fact]
    public void ExtraRewardsKey_IsKnown()
    {
        byte[] save = Inject(Fixture("three-player.save"), r =>
            r["pre_finished_room"] = new JsonObject { ["extra_rewards"] = new JsonObject { [Newbie.ToString()] = new JsonArray() } });
        Assert.Contains("$.pre_finished_room.extra_rewards{7000003}", SaveScan.FindReferences(save, Newbie));
        Assert.Empty(SaveScan.FindUnknownReferences(save, Newbie));
    }

    [Fact]
    public void UnknownReferences_AsNumberStringOrKey_AreFoundWithTheirPaths()
    {
        byte[] save = Inject(Fixture("three-player.save"), r =>
        {
            r["future_owner"] = Newbie;                                         // number
            r["future_list"] = new JsonArray(new JsonObject { ["who"] = "7000003" }); // string
            r["future_map"] = new JsonObject { ["7000003"] = true };             // key
        });
        var unknown = SaveScan.FindUnknownReferences(save, Newbie);
        Assert.Equal(new[] { "$.future_owner", "$.future_list[0].who", "$.future_map{7000003}" }, unknown);
        Assert.Empty(SaveScan.FindUnknownReferences(save, Tester));
    }

    [Fact]
    public void SimilarNumbers_AreNotReferences()
    {
        byte[] save = Inject(Fixture("three-player.save"), r =>
        {
            r["a"] = 70000031;
            r["b"] = "7000003x";
            r["c"] = 7000003.5;
        });
        Assert.Empty(SaveScan.FindUnknownReferences(save, Newbie));
    }

    // ---- through RosterEdit: a reference in a place the edits don't handle refuses every edit

    static SerializableRun WithCardFloor(ulong value)
    {
        var run = PartyRunSave.FromBytes(Fixture("three-player.save"));
        run.Players[0].Deck[0].FloorAddedToDeck = (int)value; // a field that should never hold a player ID
        return run;
    }

    [Fact]
    public void Bench_RefusedWhenTheSaveMentionsThePlayerSomewhereUnknown()
    {
        var run = WithCardFloor(Newbie);
        var party = ThreeParty();
        var result = RosterEdit.Bench(run, party, "newbie");
        Assert.False(result.Ok);
        Assert.Contains("$.players[0].deck[0].floor_added_to_deck", result.Refusal);
        Assert.Contains("Nothing was changed", result.Refusal);
        Assert.Equal(3, run.Players.Count);
        Assert.Equal(SeatState.Active, party.Seats.Single(s => s.SeatId == "newbie").State);
    }

    [Fact]
    public void Remove_RefusedWhenTheSaveMentionsThePlayerSomewhereUnknown()
    {
        var run = WithCardFloor(Tester);
        Assert.Contains("floor_added_to_deck", RosterEdit.Remove(run, ThreeParty(), "tester").Refusal);
    }

    [Fact]
    public void Add_RefusedWhenTheNewIdAlreadyAppearsInTheSave()
    {
        var run = WithCardFloor(Newbie + 1);
        Assert.Null(RosterEdit.PlayerIdForAdd(run, ThreeParty(), "charlie", out var refusal));
        Assert.Contains("player 7000004", refusal);
    }

    [Fact]
    public void Restore_RefusedWhenTheSaveMentionsThePlayerSomewhereUnknown()
    {
        var run = PartyRunSave.FromBytes(Fixture("three-player.save"));
        var party = ThreeParty();
        var bench = RosterEdit.Bench(run, party, "newbie");
        Assert.True(bench.Ok, bench.Refusal);
        run.Players[0].Deck[0].FloorAddedToDeck = (int)Newbie;
        var restore = RosterEdit.Restore(run, party, "newbie", bench.BenchedPlayer!);
        Assert.False(restore.Ok);
        Assert.Equal(2, run.Players.Count);
    }

    [Fact]
    public void EditsOnOtherPlayers_AreNotBlocked()
    {
        var run = WithCardFloor(Newbie);
        Assert.True(RosterEdit.Bench(run, ThreeParty(), "tester").Ok);
    }
}
