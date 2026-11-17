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
        FailedAttemptLimiter? limiter = null, string[]? connected = null, int cap = 4) =>
        HostHandshake.Decide(Host, version, game, commit, seat, token, Addr, party,
            new LobbyState(open, cap, connected ?? Array.Empty<string>()), limiter ?? new FailedAttemptLimiter());

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
}
