using System.Collections.Generic;
using System.Linq;

namespace SpireParty.Core;

/// <summary>What the host knows about itself when judging a hello.</summary>
public sealed record HostIdentity(string SpirePartyVersion, string GameVersion, string GameCommit);

/// <summary>
/// Live lobby state the decision depends on. <paramref name="RunPlayerIds"/> is null when no saved run
/// is loaded; otherwise only seats whose player is in the saved run may join. <paramref name="RunInProgress"/>: the
/// host is playing a run (not in a lobby); every hello is refused then (task 3.13).
/// </summary>
public sealed record LobbyState(bool OpenForNewSeats, int PlayerCap, IReadOnlyCollection<string> ConnectedSeatIds,
                                IReadOnlyCollection<ulong>? RunPlayerIds = null, bool RunInProgress = false);

public abstract record HandshakeDecision;
/// <param name="NewToken">Shown once to the joining player: set for a new seat and for a claimed open seat.</param>
/// <param name="Created">True when this hello created the seat (false for an existing seat, open or not).</param>
public sealed record Admit(Seat Seat, string? NewToken, bool Created = false) : HandshakeDecision;
public sealed record Deny(string Code, string Reason) : HandshakeDecision;

/// <summary>
/// The host's answer to a hello, as a pure function over the in-memory party (it may add a seat, or set
/// the token of an open seat). Every refusal has a code and a readable reason the joining player sees,
/// and a refusal never changes the party.
/// <para>An <b>open</b> seat has no token yet (empty <see cref="Seat.TokenHash"/>): seats added in the
/// roster editor and seats whose token the host reset (DESIGN → Identity, M2 decision D1). The next hello for
/// it claims it, whatever token was typed, and gets a new token.</para>
/// </summary>
public static class HostHandshake
{
    public const string RunInProgressReason =
        "The party is in the middle of a run. Ask the host to use Edit roster (or Save and Quit) to let you in.";

    public static HandshakeDecision Decide(
        HostIdentity host, string spirePartyVersion, string gameVersion, string gameCommit,
        string rawSeatId, string token, string address,
        PartyFile party, LobbyState lobby, FailedAttemptLimiter limiter)
    {
        if (spirePartyVersion != host.SpirePartyVersion)
            return new Deny("spireparty_version",
                $"The host runs SpireParty {host.SpirePartyVersion}, you run {spirePartyVersion}. Install the same version.");

        if (gameVersion != host.GameVersion || gameCommit != host.GameCommit)
            return new Deny("game_version",
                $"The host runs game {host.GameVersion} ({host.GameCommit}), you run {gameVersion} ({gameCommit}). " +
                "Update the game, and make sure you are both on the same Steam branch.");

        if (limiter.IsBlocked(address))
            return new Deny("rate_limited", "Too many failed attempts from your address. Wait a minute and try again.");

        // Mid-run nobody can join (the game would refuse with its generic "run in progress"), so refuse before
        // any seat is created or claimed: party.json stays unchanged.
        if (lobby.RunInProgress)
            return new Deny("run_in_progress", RunInProgressReason);

        string seatId = SeatRules.Normalize(rawSeatId);
        if (!SeatRules.IsValidSeatId(seatId))
            return new Deny("bad_seat_id",
                $"\"{rawSeatId}\" is not a valid seat ID: use 1 to {SeatRules.MaxSeatIdLength} letters, digits or dashes, starting with a letter.");

        if (seatId == PartyStore.HostSeatId)
            return new Deny("host_seat", "The host seat cannot be claimed from another game.");

        var seat = party.Seats.FirstOrDefault(s => s.SeatId == seatId);
        if (seat == null)
        {
            if (!lobby.OpenForNewSeats)
                return new Deny("lobby_closed",
                    $"There is no seat \"{seatId}\" and the host is not taking new players right now.");
            if (party.Seats.Count(s => s.State == SeatState.Active) >= lobby.PlayerCap)
                return new Deny("party_full", $"The party is full ({lobby.PlayerCap} players).");

            string newToken = SeatTokens.Generate();
            seat = new Seat
            {
                SeatId = seatId,
                PlayerId = SeatRules.AllocatePlayerId(party),
                TokenHash = SeatTokens.Hash(newToken),
                DisplayName = seatId,
            };
            party.Seats.Add(seat);
            limiter.RecordSuccess(address);
            return new Admit(seat, newToken, Created: true);
        }

        bool isOpen = seat.TokenHash.Length == 0;
        if (!isOpen && !SeatTokens.Verify(token, seat.TokenHash))
        {
            limiter.RecordFailure(address);
            return new Deny("wrong_token", string.IsNullOrWhiteSpace(token)
                ? $"Seat \"{seatId}\" already exists. Enter its token (the host can reset it)."
                : $"Wrong token for seat \"{seatId}\".");
        }

        if (seat.State == SeatState.Benched)
            return new Deny("seat_benched", $"Seat \"{seatId}\" is benched. Ask the host to restore it first.");
        if (seat.State == SeatState.Empty)
            return new Deny("seat_empty", $"Seat \"{seatId}\" was removed from this party.");
        if (lobby.RunPlayerIds != null && !lobby.RunPlayerIds.Contains(seat.PlayerId))
            return new Deny("not_in_run",
                $"Seat \"{seatId}\" is not part of this party's saved run. Ask the host to add it from the Roster screen.");
        if (lobby.ConnectedSeatIds.Contains(seatId))
            return new Deny("seat_in_use", $"Seat \"{seatId}\" is already connected from another game.");

        limiter.RecordSuccess(address);
        if (!isOpen) return new Admit(seat, null);

        string token2 = SeatTokens.Generate();
        seat.TokenHash = SeatTokens.Hash(token2);
        return new Admit(seat, token2);
    }
}
