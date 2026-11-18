using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SpireParty.Core;

/// <summary>
/// Outcome of one roster edit. On a refusal nothing was changed. On success the caller writes the save,
/// <c>party.json</c>, and the seat's <c>benched/&lt;seat&gt;.json</c> as the flags say.
/// </summary>
public sealed class RosterResult
{
    public string? Refusal { get; private init; }
    public bool Ok => Refusal == null;
    /// <summary>Bench: the player's state, to store as <c>benched/&lt;seat&gt;.json</c>.</summary>
    public SerializablePlayer? BenchedPlayer { get; private init; }
    /// <summary>Restore, or remove of a benched seat: delete <c>benched/&lt;seat&gt;.json</c>.</summary>
    public bool DeleteBenchedFile { get; private init; }
    /// <summary>Restore and add: empty history entries added for past floors.</summary>
    public int Backfilled { get; private init; }

    internal static RosterResult Refuse(string reason) => new() { Refusal = reason };
    internal static RosterResult Done(SerializablePlayer? benched = null, bool deleteBenched = false, int backfilled = 0) =>
        new() { BenchedPlayer = benched, DeleteBenchedFile = deleteBenched, Backfilled = backfilled };
}

/// <summary>
/// Roster edits on a saved run and its party, in memory (DESIGN → Join and leave; findings → Player
/// references, Save-surgery spike, M2). Every method checks everything first and changes nothing when it
/// refuses. Rules:
/// <list type="bullet">
/// <item>Players are only ever appended; existing slot order never changes (it seeds per-player RNG).</item>
/// <item>A restored or added player gets an empty history entry on every past floor.</item>
/// <item>Bench and remove keep the player's history and map drawings; their <c>extra_rewards</c> key goes,
/// and an edit is refused while that key holds unclaimed rewards.</item>
/// <item>At least two players stay in the run; the host seat is never benched or removed.</item>
/// <item>At most <see cref="Cap"/> players in the run.</item>
/// </list>
/// A seat that is <c>Active</c> in party.json but not in the save (it joined the lobby, then left before
/// the run started) is allowed; it can be added to the run like a new seat.
/// </summary>
public static class RosterEdit
{
    /// <summary>
    /// Most players the game can carry: <see cref="WireWidths.MaxPlayers"/> (8) with the widened lobby
    /// format, set back to the vanilla 4 at startup if that patch could not be applied.
    /// </summary>
    public static int GameCap { get; set; } = WireWidths.MaxPlayers;
    public const int VanillaCap = 4;
    public const int MinPlayers = 2;

    public static int Cap(PartyFile party) => Cap(party, GameCap);
    public static int Cap(PartyFile party, int gameCap) => Math.Min(party.MaxPlayers, gameCap);

    public static RosterResult Bench(SerializableRun run, PartyFile party, string seatId)
    {
        if (CheckLeave(run, party, seatId, "bench", out var seat, out var player) is { } refusal) return RosterResult.Refuse(refusal);
        if (player == null) return RosterResult.Refuse($"Seat \"{seat!.SeatId}\" is not in the run, so there is nothing to bench.");

        RemoveFromRun(run, player);
        seat!.State = SeatState.Benched;
        return RosterResult.Done(benched: player);
    }

    public static RosterResult Remove(SerializableRun run, PartyFile party, string seatId)
    {
        var seat = Find(party, seatId);
        if (seat?.State == SeatState.Benched)
        {
            if (Consistency(run, party) is { } bad) return RosterResult.Refuse(bad);
            seat.State = SeatState.Empty;
            seat.TokenHash = "";
            return RosterResult.Done(deleteBenched: true);
        }

        if (CheckLeave(run, party, seatId, "remove", out seat, out var player) is { } refusal) return RosterResult.Refuse(refusal);
        if (player != null) RemoveFromRun(run, player);
        seat!.State = SeatState.Empty;
        seat.TokenHash = "";
        return RosterResult.Done();
    }

    public static RosterResult Restore(SerializableRun run, PartyFile party, string seatId, SerializablePlayer benched)
    {
        if (Consistency(run, party) is { } bad) return RosterResult.Refuse(bad);
        var seat = Find(party, seatId);
        if (seat == null) return RosterResult.Refuse($"There is no seat \"{SeatRules.Normalize(seatId)}\".");
        if (seat.State != SeatState.Benched) return RosterResult.Refuse($"Seat \"{seat.SeatId}\" is not benched.");
        if (benched.NetId != seat.PlayerId)
            return RosterResult.Refuse($"The benched file for \"{seat.SeatId}\" holds player {benched.NetId}, but the seat is player {seat.PlayerId}.");
        if (run.Players.Any(p => p.NetId == benched.NetId))
            return RosterResult.Refuse($"Player {benched.NetId} (\"{seat.SeatId}\") is already in the run.");
        if (run.Players.Count + 1 > Cap(party))
            return RosterResult.Refuse($"The run already has {run.Players.Count} players; the limit is {Cap(party)}.");

        if (Scan(run, benched.NetId, "restore", seat.SeatId) is { } unknown) return RosterResult.Refuse(unknown);

        int backfilled = AppendToRun(run, benched);
        seat.State = SeatState.Active;
        return RosterResult.Done(deleteBenched: true, backfilled: backfilled);
    }

    /// <summary>
    /// The player ID a new player for <paramref name="seatId"/> must be built with, or a refusal.
    /// Call before building the player; changes nothing.
    /// </summary>
    public static ulong? PlayerIdForAdd(SerializableRun run, PartyFile party, string seatId, out string? refusal)
    {
        refusal = CheckAdd(run, party, seatId, out var seat);
        if (refusal != null) return null;
        return seat?.PlayerId ?? SeatRules.PeekPlayerId(party);
    }

    /// <summary>
    /// Adds a newly built player (see <see cref="NewPlayer"/>) for a new seat, or for an active seat that is
    /// not in the run yet. Apply <see cref="CatchUp"/> to the player first.
    /// </summary>
    public static RosterResult Add(SerializableRun run, PartyFile party, string seatId, string displayName, SerializablePlayer player)
    {
        if (CheckAdd(run, party, seatId, out var seat) is { } refusal) return RosterResult.Refuse(refusal);
        ulong expected = seat?.PlayerId ?? SeatRules.PeekPlayerId(party);
        if (player.NetId != expected)
            return RosterResult.Refuse($"The new player was built as player {player.NetId}, but seat \"{SeatRules.Normalize(seatId)}\" needs player {expected}.");

        int backfilled = AppendToRun(run, player);
        if (seat == null)
        {
            ulong id = SeatRules.AllocatePlayerId(party);
            seat = new Seat
            {
                SeatId = SeatRules.Normalize(seatId),
                PlayerId = id,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? SeatRules.Normalize(seatId) : displayName.Trim(),
            };
            party.Seats.Add(seat);
        }
        seat.State = SeatState.Active;
        seat.Character = player.CharacterId?.ToString() ?? "";
        return RosterResult.Done(backfilled: backfilled);
    }

    /// <summary>
    /// Makes a seat open (D1): its token is cleared, and the next player to join with that seat ID gets a
    /// new one. Only changes party.json, so it works with or without a run in progress.
    /// </summary>
    public static RosterResult ResetToken(PartyFile party, string seatId)
    {
        var seat = Find(party, seatId);
        if (seat == null) return RosterResult.Refuse($"There is no seat \"{SeatRules.Normalize(seatId)}\".");
        if (seat.PlayerId == SeatRules.HostPlayerId) return RosterResult.Refuse("The host seat has no token.");
        if (seat.State == SeatState.Empty) return RosterResult.Refuse($"Seat \"{seat.SeatId}\" was removed.");
        seat.TokenHash = "";
        return RosterResult.Done();
    }

    /// <summary>A seat with no token yet: the next join with its seat ID claims it.</summary>
    public static bool IsOpen(Seat seat) => seat.PlayerId != SeatRules.HostPlayerId && seat.TokenHash.Length == 0;

    /// <summary>Null when every player in the save belongs to an active seat and the host is in the run.</summary>
    public static string? Consistency(SerializableRun run, PartyFile party)
    {
        if (run.Players.All(p => p.NetId != SeatRules.HostPlayerId))
            return "The host (player 1) is not in the saved run. Restore a backup.";
        foreach (var p in run.Players)
        {
            var seat = party.Seats.FirstOrDefault(s => s.PlayerId == p.NetId);
            if (seat == null)
                return $"Player {p.NetId} is in the saved run but has no seat in party.json. Restore a backup.";
            if (seat.State != SeatState.Active)
                return $"Seat \"{seat.SeatId}\" is {seat.State.ToString().ToLowerInvariant()} in party.json but still in the saved run. Restore a backup.";
        }
        return null;
    }

    static string? CheckLeave(SerializableRun run, PartyFile party, string seatId, string verb,
                              out Seat? seat, out SerializablePlayer? player)
    {
        player = null;
        seat = Find(party, seatId);
        if (Consistency(run, party) is { } bad) return bad;
        if (seat == null) return $"There is no seat \"{SeatRules.Normalize(seatId)}\".";
        if (seat.PlayerId == SeatRules.HostPlayerId) return $"The host seat cannot be {Past(verb)}.";
        if (seat.State != SeatState.Active) return $"Seat \"{seat.SeatId}\" is {seat.State.ToString().ToLowerInvariant()}, not active.";

        var s = seat;
        player = run.Players.FirstOrDefault(p => p.NetId == s.PlayerId);
        if (player == null) return Scan(run, seat.PlayerId, verb, seat.SeatId); // active seat that never entered the run

        if (run.Players.Count - 1 < MinPlayers)
            return $"Cannot {verb} \"{seat.SeatId}\": a co-op run needs at least {MinPlayers} players to continue, and only {run.Players.Count} are in it.";
        if (run.PreFinishedRoom?.ExtraRewards is { } extra
            && extra.TryGetValue(seat.PlayerId, out var rewards) && rewards is { Count: > 0 })
            return $"Cannot {verb} \"{seat.SeatId}\": they have {rewards.Count} unclaimed reward(s) from the last room. Continue the run, take them, and try again at the next room.";
        return Scan(run, seat.PlayerId, verb, seat.SeatId);
    }

    static string? CheckAdd(SerializableRun run, PartyFile party, string seatId, out Seat? seat)
    {
        seat = Find(party, seatId);
        if (Consistency(run, party) is { } bad) return bad;
        string id = SeatRules.Normalize(seatId);
        if (!SeatRules.IsValidSeatId(id))
            return $"\"{seatId}\" is not a valid seat ID: use 1 to {SeatRules.MaxSeatIdLength} letters, digits or dashes, starting with a letter.";
        if (seat != null)
        {
            var s = seat;
            switch (seat.State)
            {
                case SeatState.Benched: return $"Seat \"{id}\" is benched. Restore it instead.";
                case SeatState.Empty: return $"Seat \"{id}\" was removed. Pick a new seat ID.";
            }
            if (run.Players.Any(p => p.NetId == s.PlayerId)) return $"Seat \"{id}\" is already in the run.";
        }
        if (run.Players.Count + 1 > Cap(party))
            return $"The run already has {run.Players.Count} players; the limit is {Cap(party)}.";
        return Scan(run, seat?.PlayerId ?? SeatRules.PeekPlayerId(party), "add", id);
    }

    /// <summary>The fail-closed scan (<see cref="SaveScan"/>) as a refusal, or null when the save is clean.</summary>
    static string? Scan(SerializableRun run, ulong playerId, string verb, string seatId)
    {
        var unknown = SaveScan.FindUnknownReferences(PartyRunSave.ToBytes(run), playerId);
        if (unknown.Count == 0) return null;
        string where = string.Join(", ", unknown.Take(3)) + (unknown.Count > 3 ? $" and {unknown.Count - 3} more" : "");
        return $"Cannot {verb} \"{seatId}\": the save mentions player {playerId} at {where}, which SpireParty does not know how to " +
               "clean up (the game may have changed). Nothing was changed.";
    }

    static Seat? Find(PartyFile party, string seatId)
    {
        string id = SeatRules.Normalize(seatId);
        return party.Seats.FirstOrDefault(s => s.SeatId == id);
    }

    static string Past(string verb) => verb == "bench" ? "benched" : "removed";

    static void RemoveFromRun(SerializableRun run, SerializablePlayer player)
    {
        run.Players.Remove(player);
        run.PreFinishedRoom?.ExtraRewards?.Remove(player.NetId);
        // History entries and map drawings stay: the game tolerates them (save-surgery spike).
    }

    static int AppendToRun(SerializableRun run, SerializablePlayer player)
    {
        run.Players.Add(player);
        int backfilled = 0;
        foreach (var entry in run.MapPointHistory.SelectMany(act => act))
        {
            if (entry.PlayerStats.All(s => s.PlayerId != player.NetId))
            {
                entry.PlayerStats.Add(new PlayerMapPointHistoryEntry { PlayerId = player.NetId });
                backfilled++;
            }
        }
        return backfilled;
    }
}

/// <summary>Catch-up for a new player (DESIGN → Join and leave; defaults in party.json → catchUp).</summary>
public static class CatchUp
{
    /// <summary>
    /// Raises gold and max HP per the party's rules, measured against the players already in the run.
    /// Never lowers a value below the starter's. Current HP is set to the (new) max HP.
    /// </summary>
    public static void Apply(SerializablePlayer player, IReadOnlyCollection<SerializablePlayer> party, string goldRule, string maxHpRule)
    {
        var others = party.Where(p => p.NetId != player.NetId).ToList();
        if (others.Count > 0)
        {
            int gold = goldRule switch
            {
                "highest" => others.Max(p => p.Gold),
                "average" => (int)Math.Floor(others.Average(p => p.Gold)),
                "lowest" => others.Min(p => p.Gold),
                _ => player.Gold, // "starter"
            };
            player.Gold = Math.Max(player.Gold, gold);
            if (maxHpRule == "matchLowest") player.MaxHp = Math.Max(player.MaxHp, others.Min(p => p.MaxHp));
        }
        player.CurrentHp = player.MaxHp;
    }

    /// <summary>
    /// Extra Ascender's Banes a Dread joiner gets: one per act entered after the first, as
    /// <c>DreadRunModel.AfterActEntered</c> gives everyone else. The ascension-level bane comes from the
    /// game's own setup in <see cref="NewPlayer"/>.
    /// </summary>
    public static int DreadBanes(SerializableRun run) => run.GameMode == GameMode.Dread ? Math.Max(0, run.CurrentActIndex) : 0;
}
