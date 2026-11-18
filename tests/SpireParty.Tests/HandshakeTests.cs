using SpireParty.Core;

namespace SpireParty.Tests;

public class HandshakeTests
{
    static readonly HostIdentity Host = new("0.1.0", "v0.113.1", "d09d48e5");
    const string Addr = "100.64.0.9";

    static PartyFile NewParty()
    {
        var p = new PartyFile { Name = "gate" };
        p.Seats.Add(new Seat { SeatId = "host", PlayerId = SeatRules.HostPlayerId });
        return p;
    }

    static HandshakeDecision Decide(PartyFile party, string seat, string token = "", bool open = true,
        string version = "0.1.0", string game = "v0.113.1", string commit = "d09d48e5",
        FailedAttemptLimiter? limiter = null, string[]? connected = null, int cap = 4, ulong[]? run = null) =>
        HostHandshake.Decide(Host, version, game, commit, seat, token, Addr, party,
            new LobbyState(open, cap, connected ?? Array.Empty<string>(), run), limiter ?? new FailedAttemptLimiter());

    static string Code(HandshakeDecision d) => Assert.IsType<Deny>(d).Code;

    [Fact]
    public void NewSeat_WhileOpen_GetsIdAndToken_ThenRejoinsWithToken()
    {
        var party = NewParty();
        var first = Assert.IsType<Admit>(Decide(party, " Ravi "));
        Assert.Equal("ravi", first.Seat.SeatId);
        Assert.Equal(SeatRules.FirstGuestPlayerId, first.Seat.PlayerId);
        Assert.NotNull(first.NewToken);

        var again = Assert.IsType<Admit>(Decide(party, "ravi", first.NewToken!, open: false));
        Assert.Equal(first.Seat.PlayerId, again.Seat.PlayerId);
        Assert.Null(again.NewToken);
    }

    [Fact]
    public void VersionAndBuildMismatch_AreRefusedWithBothVersionsNamed()
    {
        var d = Assert.IsType<Deny>(Decide(NewParty(), "ravi", version: "0.0.9"));
        Assert.Equal("spireparty_version", d.Code);
        Assert.Contains("0.1.0", d.Reason);
        Assert.Contains("0.0.9", d.Reason);
        Assert.Equal("game_version", Code(Decide(NewParty(), "ravi", commit: "ffffffff")));
        Assert.Equal("game_version", Code(Decide(NewParty(), "ravi", game: "v0.112.0")));
    }

    [Fact]
    public void BadSeatId_HostSeat_ClosedLobby_FullParty()
    {
        Assert.Equal("bad_seat_id", Code(Decide(NewParty(), "no spaces")));
        Assert.Equal("host_seat", Code(Decide(NewParty(), "host")));
        Assert.Equal("lobby_closed", Code(Decide(NewParty(), "ravi", open: false)));

        var party = NewParty();
        Decide(party, "a"); Decide(party, "b"); Decide(party, "c");
        Assert.Equal("party_full", Code(Decide(party, "d")));
    }

    [Fact]
    public void ExistingSeat_WrongOrMissingToken_IsRefused_AndCountsTowardRateLimit()
    {
        var party = NewParty();
        var token = Assert.IsType<Admit>(Decide(party, "ravi")).NewToken!;
        var limiter = new FailedAttemptLimiter(maxFailures: 2);

        Assert.Contains("Enter its token", Assert.IsType<Deny>(Decide(party, "ravi", "", limiter: limiter)).Reason);
        Assert.Equal("wrong_token", Code(Decide(party, "ravi", "wrong-token-11", limiter: limiter)));
        Assert.Equal("rate_limited", Code(Decide(party, "ravi", token, limiter: limiter)));
    }

    [Fact]
    public void BenchedRemovedOrAlreadyConnectedSeats_AreRefused()
    {
        var party = NewParty();
        var admit = Assert.IsType<Admit>(Decide(party, "ravi"));
        Assert.Equal("seat_in_use", Code(Decide(party, "ravi", admit.NewToken!, connected: new[] { "ravi" })));

        admit.Seat.State = SeatState.Benched;
        Assert.Equal("seat_benched", Code(Decide(party, "ravi", admit.NewToken!)));
        admit.Seat.State = SeatState.Empty;
        Assert.Equal("seat_empty", Code(Decide(party, "ravi", admit.NewToken!)));
    }

    // ---- open seats (M2 task 2.5, decision D1)

    static PartyFile PartyWithSeat(string tokenHash, SeatState state = SeatState.Active)
    {
        var p = NewParty();
        p.Seats.Add(new Seat { SeatId = "ravi", PlayerId = 7_000_002, TokenHash = tokenHash, State = state });
        p.NextPlayerId = 7_000_003;
        return p;
    }

    [Theory]
    [InlineData("")]
    [InlineData("whatever-they-typed")]
    public void OpenSeat_IsClaimedWithANewToken_EvenWhileClosed(string typed)
    {
        var party = PartyWithSeat("");
        var admit = Assert.IsType<Admit>(Decide(party, "ravi", typed, open: false));
        Assert.False(admit.Created);
        Assert.Equal(7_000_002UL, admit.Seat.PlayerId);
        Assert.NotNull(admit.NewToken);
        Assert.True(SeatTokens.Verify(admit.NewToken, party.Seats[1].TokenHash));
        Assert.Equal(7_000_003UL, party.NextPlayerId); // no ID handed out

        // Claimed now: the next empty-token hello is refused, the new token works.
        Assert.Equal("wrong_token", Code(Decide(party, "ravi", "", open: false)));
        Assert.Null(Assert.IsType<Admit>(Decide(party, "ravi", admit.NewToken!, open: false)).NewToken);
    }

    [Fact]
    public void ResetToken_ThenTheOldTokenNoLongerMatters()
    {
        var party = PartyWithSeat(SeatTokens.Hash("amber-otter-41"));
        Assert.True(RosterEdit.ResetToken(party, "RAVI").Ok);
        Assert.True(RosterEdit.IsOpen(party.Seats[1]));
        var admit = Assert.IsType<Admit>(Decide(party, "ravi", "amber-otter-41", open: false));
        Assert.NotNull(admit.NewToken);
        Assert.NotEqual("amber-otter-41", admit.NewToken);
    }

    [Fact]
    public void ResetToken_RefusedForHostRemovedAndUnknownSeats()
    {
        var party = PartyWithSeat("x", SeatState.Empty);
        Assert.Contains("host seat", RosterEdit.ResetToken(party, "host").Refusal);
        Assert.Contains("removed", RosterEdit.ResetToken(party, "ravi").Refusal);
        Assert.Contains("no seat", RosterEdit.ResetToken(party, "nobody").Refusal);
        Assert.False(RosterEdit.IsOpen(party.Seats[0])); // the host seat has no token but is never open
    }

    [Theory]
    [InlineData(SeatState.Benched, "seat_benched")]
    [InlineData(SeatState.Empty, "seat_empty")]
    public void OpenSeat_BenchedOrRemoved_IsRefusedAndStaysOpen(SeatState state, string code)
    {
        var party = PartyWithSeat("", state);
        Assert.Equal(code, Code(Decide(party, "ravi")));
        Assert.Equal("", party.Seats[1].TokenHash);
    }

    [Fact]
    public void OpenSeat_AlreadyConnected_IsRefusedAndStaysOpen()
    {
        var party = PartyWithSeat("");
        Assert.Equal("seat_in_use", Code(Decide(party, "ravi", connected: new[] { "ravi" })));
        Assert.Equal("", party.Seats[1].TokenHash);
    }

    [Fact]
    public void SavedRun_OnlySeatsInTheRunMayJoin_AndARefusalChangesNothing()
    {
        var party = PartyWithSeat(""); // open seat, not in the run
        Assert.Equal("not_in_run", Code(Decide(party, "ravi", open: false, run: new[] { 1UL })));
        Assert.Equal("", party.Seats[1].TokenHash);

        Assert.IsType<Admit>(Decide(party, "ravi", open: false, run: new[] { 1UL, 7_000_002UL }));
    }

    [Fact]
    public void NewSeat_IsMarkedCreated()
    {
        Assert.True(Assert.IsType<Admit>(Decide(NewParty(), "ravi")).Created);
    }
}
