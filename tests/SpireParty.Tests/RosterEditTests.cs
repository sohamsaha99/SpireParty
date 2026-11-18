using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using SpireParty.Core;

namespace SpireParty.Tests;

/// <summary>
/// Roster edits on real saves made by us (fixtures/): two-player.save is the M1 gate run (players 1 and
/// 7000002, Act 2); three-player.save is the 2.1 spike run (1, 7000002, 7000003). Both standard, A0.
/// </summary>
public class RosterEditTests
{
    const ulong Tester = 7_000_002, Newbie = 7_000_003;

    static SerializableRun Load(string name) =>
        PartyRunSave.FromBytes(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name)));

    static PartyFile Party(params (string Seat, ulong Id)[] guests)
    {
        var p = new PartyFile { Name = "test" };
        p.Seats.Add(new Seat { SeatId = PartyStore.HostSeatId, PlayerId = SeatRules.HostPlayerId, DisplayName = "Host" });
        foreach (var (seat, id) in guests)
            p.Seats.Add(new Seat { SeatId = seat, PlayerId = id, DisplayName = seat, TokenHash = "sha256$x$y" });
        p.NextPlayerId = guests.Select(g => g.Id).DefaultIfEmpty(SeatRules.FirstGuestPlayerId - 1).Max() + 1;
        return p;
    }

    static PartyFile ThreeParty() => Party(("tester", Tester), ("newbie", Newbie));
    static PartyFile TwoParty() => Party(("tester", Tester));

    static SerializablePlayer Clone(SerializablePlayer p) => PartyRunSave.PlayerFromBytes(PartyRunSave.PlayerToBytes(p));

    static int Floors(SerializableRun run) => run.MapPointHistory.Sum(act => act.Count);

    static SerializablePlayer FreshPlayer(SerializableRun run, ulong netId, int gold = 99, int maxHp = 70)
    {
        var p = Clone(run.Players[0]);
        p.NetId = netId;
        p.Gold = gold;
        p.MaxHp = maxHp;
        p.CurrentHp = maxHp;
        return p;
    }

    static ulong[] Ids(SerializableRun run) => run.Players.Select(p => p.NetId).ToArray();

    // ---- fixtures and round trip

    [Fact]
    public void Fixtures_LoadWithExpectedPlayers()
    {
        Assert.Equal(new[] { 1UL, Tester }, Ids(Load("two-player.save")));
        Assert.Equal(new[] { 1UL, Tester, Newbie }, Ids(Load("three-player.save")));
    }

    [Fact]
    public void Fixtures_AreConsistentWithTheirParties()
    {
        Assert.Null(RosterEdit.Consistency(Load("two-player.save"), TwoParty()));
        Assert.Null(RosterEdit.Consistency(Load("three-player.save"), ThreeParty()));
    }

    // ---- bench and restore

    [Fact]
    public void BenchLastThenRestore_IsByteIdenticalToNoEdit()
    {
        var run = Load("three-player.save");
        var party = ThreeParty();
        byte[] before = PartyRunSave.ToBytes(run);

        var bench = RosterEdit.Bench(run, party, "newbie");
        Assert.True(bench.Ok, bench.Refusal);
        Assert.Equal(new[] { 1UL, Tester }, Ids(run));
        Assert.Equal(SeatState.Benched, party.Seats.Single(s => s.SeatId == "newbie").State);

        // Through the benched file, as the editor stores it.
        var stored = PartyRunSave.PlayerFromBytes(PartyRunSave.PlayerToBytes(bench.BenchedPlayer!));
        var restore = RosterEdit.Restore(run, party, "newbie", stored);
        Assert.True(restore.Ok, restore.Refusal);
        Assert.True(restore.DeleteBenchedFile);
        Assert.Equal(0, restore.Backfilled); // history was kept while benched
        Assert.Equal(SeatState.Active, party.Seats.Single(s => s.SeatId == "newbie").State);
        Assert.Equal(before, PartyRunSave.ToBytes(run));
    }

    [Fact]
    public void BenchMiddleThenRestore_AppendsAndKeepsThePlayerUnchanged()
    {
        var run = Load("three-player.save");
        var party = ThreeParty();
        byte[] testerBefore = PartyRunSave.PlayerToBytes(run.Players[1]);

        var bench = RosterEdit.Bench(run, party, "TESTER");
        Assert.True(bench.Ok, bench.Refusal);
        var restore = RosterEdit.Restore(run, party, "tester", bench.BenchedPlayer!);
        Assert.True(restore.Ok, restore.Refusal);

        Assert.Equal(new[] { 1UL, Newbie, Tester }, Ids(run)); // appended, never reordered in place
        Assert.Equal(testerBefore, PartyRunSave.PlayerToBytes(run.Players[2]));
    }

    [Fact]
    public void Bench_KeepsHistoryAndRemovesEmptyExtraRewardsKey()
    {
        var run = Load("three-player.save");
        (run.PreFinishedRoom ??= new SerializableRoom()).ExtraRewards[Newbie] = new List<SerializableReward>();
        int historyBefore = run.MapPointHistory.SelectMany(a => a).Count(e => e.PlayerStats.Any(s => s.PlayerId == Newbie));

        Assert.True(RosterEdit.Bench(run, ThreeParty(), "newbie").Ok);

        Assert.False(run.PreFinishedRoom!.ExtraRewards.ContainsKey(Newbie));
        Assert.Equal(historyBefore, run.MapPointHistory.SelectMany(a => a).Count(e => e.PlayerStats.Any(s => s.PlayerId == Newbie)));
    }

    [Fact]
    public void Bench_RefusedWithUnclaimedExtraRewards()
    {
        var run = Load("three-player.save");
        (run.PreFinishedRoom ??= new SerializableRoom()).ExtraRewards[Newbie] = new List<SerializableReward> { new() };
        AssertRefused(RosterEdit.Bench(run, ThreeParty(), "newbie"), "unclaimed reward", run, 3);
    }

    [Fact]
    public void Bench_RefusedForHostSeat()
    {
        var run = Load("three-player.save");
        AssertRefused(RosterEdit.Bench(run, ThreeParty(), "host"), "host seat", run, 3);
    }

    [Fact]
    public void Bench_RefusedWhenFewerThanTwoPlayersWouldRemain()
    {
        var run = Load("two-player.save");
        AssertRefused(RosterEdit.Bench(run, TwoParty(), "tester"), "at least 2 players", run, 2);
    }

    [Fact]
    public void Bench_RefusedForUnknownOrAlreadyBenchedSeat()
    {
        var run = Load("three-player.save");
        var party = ThreeParty();
        AssertRefused(RosterEdit.Bench(run, party, "nobody"), "no seat", run, 3);
        Assert.True(RosterEdit.Bench(run, party, "newbie").Ok);
        AssertRefused(RosterEdit.Bench(run, party, "newbie"), "benched, not active", run, 2);
    }

    [Fact]
    public void Restore_RefusedWhenNotBenchedOrWrongPlayerOrOverCap()
    {
        var run = Load("three-player.save");
        var party = ThreeParty();
        var newbie = Clone(run.Players[2]);
        AssertRefused(RosterEdit.Restore(run, party, "newbie", newbie), "not benched", run, 3);

        var bench = RosterEdit.Bench(run, party, "newbie");
        AssertRefused(RosterEdit.Restore(run, party, "newbie", Clone(run.Players[1])), "holds player 7000002", run, 2);

        party.MaxPlayers = 2;
        AssertRefused(RosterEdit.Restore(run, party, "newbie", bench.BenchedPlayer!), "limit is 2", run, 2);
    }

    // ---- add

    [Fact]
    public void Add_NewSeat_AppendsBackfillsAndCreatesSeat()
    {
        var run = Load("two-player.save");
        var party = TwoParty();
        int floors = Floors(run);

        ulong? id = RosterEdit.PlayerIdForAdd(run, party, "Charlie", out var refusal);
        Assert.Null(refusal);
        Assert.Equal(Tester + 1, id);
        Assert.Equal(Tester + 1, party.NextPlayerId); // peek does not hand it out

        var result = RosterEdit.Add(run, party, "Charlie", "Charlie B", FreshPlayer(run, id!.Value));
        Assert.True(result.Ok, result.Refusal);
        Assert.Equal(floors, result.Backfilled);
        Assert.Equal(new[] { 1UL, Tester, Tester + 1 }, Ids(run));
        Assert.All(run.MapPointHistory.SelectMany(a => a), e => Assert.Contains(e.PlayerStats, s => s.PlayerId == Tester + 1));

        var seat = party.Seats.Single(s => s.SeatId == "charlie");
        Assert.Equal(Tester + 1, seat.PlayerId);
        Assert.Equal("Charlie B", seat.DisplayName);
        Assert.Equal(SeatState.Active, seat.State);
        Assert.Equal(run.Players[2].CharacterId!.ToString(), seat.Character);
        Assert.Equal(Tester + 2, party.NextPlayerId);
        Assert.Null(RosterEdit.Consistency(run, party));
    }

    [Fact]
    public void Add_ActiveSeatNotInRun_UsesItsPlayerId()
    {
        var run = Load("two-player.save");
        var party = Party(("tester", Tester), ("late", Tester + 5));
        Assert.Equal(Tester + 5, RosterEdit.PlayerIdForAdd(run, party, "late", out _));
        Assert.True(RosterEdit.Add(run, party, "late", "", FreshPlayer(run, Tester + 5)).Ok);
        Assert.Single(party.Seats, s => s.SeatId == "late");
    }

    [Fact]
    public void Add_RefusedForWrongIdBadSeatExistingBenchedRemovedOrFull()
    {
        var run = Load("three-player.save");
        var party = ThreeParty();
        AssertRefused(RosterEdit.Add(run, party, "charlie", "", FreshPlayer(run, 42)), "needs player 7000004", run, 3);
        AssertRefused(RosterEdit.Add(run, party, "9lives", "", FreshPlayer(run, Newbie + 1)), "not a valid seat ID", run, 3);
        AssertRefused(RosterEdit.Add(run, party, "tester", "", FreshPlayer(run, Tester)), "already in the run", run, 3);

        Assert.True(RosterEdit.Bench(run, party, "newbie").Ok);
        AssertRefused(RosterEdit.Add(run, party, "newbie", "", FreshPlayer(run, Newbie)), "Restore it instead", run, 2);
        Assert.True(RosterEdit.Remove(run, party, "newbie").Ok);
        AssertRefused(RosterEdit.Add(run, party, "newbie", "", FreshPlayer(run, Newbie)), "was removed", run, 2);

        party.MaxPlayers = 2;
        Assert.Null(RosterEdit.PlayerIdForAdd(run, party, "charlie", out var refusal));
        Assert.Contains("limit is 2", refusal);
    }

    [Fact]
    public void Cap_IsTheGameLimitUntilM3()
    {
        Assert.Equal(4, RosterEdit.Cap(new PartyFile { MaxPlayers = 8 }));
        Assert.Equal(3, RosterEdit.Cap(new PartyFile { MaxPlayers = 3 }));
    }

    // ---- remove

    [Fact]
    public void Remove_ActivePlayer_LeavesRunAndClearsToken()
    {
        var run = Load("three-player.save");
        var party = ThreeParty();
        var result = RosterEdit.Remove(run, party, "newbie");
        Assert.True(result.Ok, result.Refusal);
        Assert.False(result.DeleteBenchedFile);
        Assert.Equal(new[] { 1UL, Tester }, Ids(run));
        var seat = party.Seats.Single(s => s.SeatId == "newbie");
        Assert.Equal(SeatState.Empty, seat.State);
        Assert.Equal("", seat.TokenHash);
    }

    [Fact]
    public void Remove_BenchedSeat_DeletesBenchedFile()
    {
        var run = Load("three-player.save");
        var party = ThreeParty();
        Assert.True(RosterEdit.Bench(run, party, "newbie").Ok);
        var result = RosterEdit.Remove(run, party, "newbie");
        Assert.True(result.Ok, result.Refusal);
        Assert.True(result.DeleteBenchedFile);
        Assert.Equal(SeatState.Empty, party.Seats.Single(s => s.SeatId == "newbie").State);
    }

    [Fact]
    public void Remove_RefusedForHostAndLastGuest()
    {
        var run = Load("two-player.save");
        AssertRefused(RosterEdit.Remove(run, TwoParty(), "host"), "host seat cannot be removed", run, 2);
        AssertRefused(RosterEdit.Remove(run, TwoParty(), "tester"), "at least 2 players", run, 2);
    }

    // ---- consistency

    [Fact]
    public void Edits_RefusedWhenSaveAndPartyDisagree()
    {
        var run = Load("three-player.save");
        var noSeat = TwoParty(); // newbie is in the save but has no seat
        AssertRefused(RosterEdit.Bench(run, noSeat, "tester"), "no seat in party.json", run, 3);

        var benchedButInRun = ThreeParty();
        benchedButInRun.Seats.Single(s => s.SeatId == "newbie").State = SeatState.Benched;
        Assert.Contains("still in the saved run", RosterEdit.Consistency(run, benchedButInRun));
    }

    // ---- catch-up

    static List<SerializablePlayer> Others(SerializableRun run, params (int Gold, int MaxHp)[] stats)
    {
        var list = new List<SerializablePlayer>();
        ulong id = 1;
        foreach (var (gold, maxHp) in stats)
        {
            var p = FreshPlayer(run, id++, gold, maxHp);
            p.CurrentHp = maxHp / 2;
            list.Add(p);
        }
        return list;
    }

    [Theory]
    [InlineData("highest", 400)]
    [InlineData("average", 183)] // (400 + 50 + 101) / 3 = 183.67, floored
    [InlineData("lowest", 99)]   // lowest is 50, below the starter's 99: never lowered
    [InlineData("starter", 99)]
    public void CatchUp_Gold(string rule, int expected)
    {
        var run = Load("two-player.save");
        var joiner = FreshPlayer(run, 99, gold: 99);
        CatchUp.Apply(joiner, Others(run, (400, 80), (50, 80), (101, 80)), rule, "starterOnly");
        Assert.Equal(expected, joiner.Gold);
    }

    [Theory]
    [InlineData("matchLowest", 75, 75)]  // lowest max HP 75 is above the starter's 70 → raised
    [InlineData("matchLowest", 60, 70)]  // lowest 60 is below → starter kept
    [InlineData("starterOnly", 75, 70)]
    public void CatchUp_MaxHp_RaisedOnlyAndCurrentHpFull(string rule, int otherMax, int expected)
    {
        var run = Load("two-player.save");
        var joiner = FreshPlayer(run, 99, maxHp: 70);
        joiner.CurrentHp = 10;
        CatchUp.Apply(joiner, Others(run, (100, otherMax), (100, 90)), "starter", rule);
        Assert.Equal(expected, joiner.MaxHp);
        Assert.Equal(expected, joiner.CurrentHp);
    }

    [Fact]
    public void CatchUp_IgnoresTheJoinerItselfInThePartyList()
    {
        var run = Load("two-player.save");
        var joiner = FreshPlayer(run, 99, gold: 99);
        var party = Others(run, (150, 80));
        party.Add(joiner);
        joiner.Gold = 99;
        CatchUp.Apply(joiner, party, "lowest", "matchLowest");
        Assert.Equal(150, joiner.Gold);
    }

    [Theory]
    [InlineData(GameMode.Dread, 0, 0)]
    [InlineData(GameMode.Dread, 2, 2)]
    [InlineData(GameMode.Standard, 2, 0)]
    public void CatchUp_DreadBanes_OnePerActAfterTheFirst(GameMode mode, int actIndex, int expected)
    {
        var run = Load("two-player.save");
        run.GameMode = mode;
        run.CurrentActIndex = actIndex;
        Assert.Equal(expected, CatchUp.DreadBanes(run));
    }

    static void AssertRefused(RosterResult result, string reasonPart, SerializableRun run, int playersAfter)
    {
        Assert.False(result.Ok);
        Assert.Contains(reasonPart, result.Refusal);
        Assert.Equal(playersAfter, run.Players.Count); // a refusal changes nothing
    }
}
