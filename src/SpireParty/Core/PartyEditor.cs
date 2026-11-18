using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SpireParty.Core;

/// <summary>What the roster screen shows for one seat.</summary>
/// <param name="Status">"host", "active", "not in run", "benched" or "removed".</param>
/// <param name="Open">The seat has no token yet: the next join with its seat ID claims it.</param>
/// <param name="Character">The game's character ID ("CHARACTER.SILENT"), or "" if unknown.</param>
public sealed record SeatView(string SeatId, string Name, string Status, bool Open, string Character,
                              int? Hp, int? MaxHp, int? Gold);

/// <summary>
/// The roster editor's actions on one party folder (DESIGN → Join and leave → Host flow). Every action
/// reloads the party from disk, applies one <see cref="RosterEdit"/> in memory and commits it with
/// <see cref="RosterCommit"/> (backup first). Returns null on success or a readable reason. Never use it
/// while a session for this party is running: the session holds the party in memory.
/// </summary>
public sealed class PartyEditor
{
    /// <summary>Builds a new player for the saved run (in-game: <see cref="NewPlayer.Build"/>); injected so tests need no game.</summary>
    public delegate SerializablePlayer PlayerBuilder(string saveJson, string character, PartyFile party, ulong netId, int dreadBanes);

    readonly PartyStore _store;
    readonly PartyBackups _backups;
    readonly int _keep;
    public string Folder { get; }

    public PartyEditor(PartyStore store, PartyBackups backups, string folder, int backupsKept)
    {
        _store = store;
        _backups = backups;
        Folder = folder;
        _keep = backupsKept;
    }

    public PartyFile LoadParty() => _store.Load(Folder);

    public bool HasRun => PartyRunSave.Exists(_store.RunSavePath(Folder));

    /// <summary>The saved run, or null if there is none. Throws <see cref="InvalidDataException"/> if it cannot be read.</summary>
    public SerializableRun? LoadRun()
    {
        if (!HasRun) return null;
        var read = PartyRunSave.Read(_store.RunSavePath(Folder));
        if (!read.Success || read.SaveData == null)
            throw new InvalidDataException($"The party's run save cannot be read ({read.Status}). Restore a backup.");
        return read.SaveData;
    }

    public IReadOnlyList<SeatView> Seats(out string? problem)
    {
        problem = null;
        var party = LoadParty();
        SerializableRun? run = null;
        try { run = LoadRun(); }
        catch (InvalidDataException e) { problem = e.Message; }
        if (run != null) problem ??= RosterEdit.Consistency(run, party);

        var names = SeatNames.Of(party);
        var views = new List<SeatView>();
        foreach (var seat in party.Seats)
        {
            SerializablePlayer? player = run?.Players.FirstOrDefault(p => p.NetId == seat.PlayerId);
            if (player == null && seat.State == SeatState.Benched)
            {
                try { player = RosterCommit.ReadBenched(_store, Folder, seat.SeatId); }
                catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException or System.Text.Json.JsonException) { }
            }
            string status = seat.PlayerId == SeatRules.HostPlayerId ? "host"
                : seat.State switch
                {
                    SeatState.Benched => "benched",
                    SeatState.Empty => "removed",
                    _ => run == null || player != null ? "active" : "not in run",
                };
            views.Add(new SeatView(seat.SeatId, names.GetValueOrDefault(seat.PlayerId, seat.SeatId), status,
                RosterEdit.IsOpen(seat) && seat.State != SeatState.Empty,
                player?.CharacterId?.ToString() ?? seat.Character, player?.CurrentHp, player?.MaxHp, player?.Gold));
        }
        return views;
    }

    // ---- edits on the saved run

    public string? Bench(string seatId) => RunEdit($"bench {seatId}", seatId, (run, party) => RosterEdit.Bench(run, party, seatId));

    public string? Restore(string seatId)
    {
        SerializablePlayer benched;
        try { benched = RosterCommit.ReadBenched(_store, Folder, seatId); }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException or System.Text.Json.JsonException)
        {
            return $"Cannot restore \"{seatId}\": {e.Message}";
        }
        return RunEdit($"restore {seatId}", seatId, (run, party) => RosterEdit.Restore(run, party, seatId, benched));
    }

    /// <summary>Adds a new seat, or an active seat that is not in the run, with a new player for <paramref name="character"/>.</summary>
    public string? Add(string seatId, string name, string character, PlayerBuilder build)
    {
        SerializableRun? run;
        try { run = LoadRun(); }
        catch (InvalidDataException e) { return e.Message; }
        if (run == null) return "There is no run yet. Before a run starts, new players join from the lobby.";

        var party = LoadParty();
        ulong? id = RosterEdit.PlayerIdForAdd(run, party, seatId, out string? refusal);
        if (id == null) return refusal;

        SerializablePlayer player;
        try
        {
            player = build(File.ReadAllText(_store.RunSavePath(Folder)), character, party, id.Value, CatchUp.DreadBanes(run));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            return $"Could not create the new player: {e.Message}";
        }
        CatchUp.Apply(player, run.Players, party.CatchUpGold, party.CatchUpMaxHp);

        var result = RosterEdit.Add(run, party, seatId, SeatNames.Clean(name), player);
        if (!result.Ok) return result.Refusal;
        return RosterCommit.Write(_store, _backups, Folder, _keep, $"add {SeatRules.Normalize(seatId)}", run, party, seatId, result);
    }

    public string? Remove(string seatId)
    {
        SerializableRun? run;
        try { run = LoadRun(); }
        catch (InvalidDataException e) { return e.Message; }
        var party = LoadParty();
        if (run != null)
            return Commit($"remove {seatId}", run, party, seatId, RosterEdit.Remove(run, party, seatId));

        // No run yet: only party.json changes.
        var seat = party.Seats.FirstOrDefault(s => s.SeatId == SeatRules.Normalize(seatId));
        if (seat == null) return $"There is no seat \"{SeatRules.Normalize(seatId)}\".";
        if (seat.PlayerId == SeatRules.HostPlayerId) return "The host seat cannot be removed.";
        if (seat.State == SeatState.Empty) return $"Seat \"{seat.SeatId}\" is already removed.";
        bool wasBenched = seat.State == SeatState.Benched;
        seat.State = SeatState.Empty;
        seat.TokenHash = "";
        return RosterCommit.Write(_store, _backups, Folder, _keep, $"remove {seat.SeatId}", null, party, seat.SeatId,
            wasBenched ? RosterResult.Done(deleteBenched: true) : null);
    }

    // ---- party.json only

    public string? ResetToken(string seatId)
    {
        var party = LoadParty();
        var result = RosterEdit.ResetToken(party, seatId);
        return result.Ok ? RosterCommit.Write(_store, _backups, Folder, _keep, $"reset token {SeatRules.Normalize(seatId)}", null, party) : result.Refusal;
    }

    public string? Rename(string seatId, string name)
    {
        var party = LoadParty();
        var seat = party.Seats.FirstOrDefault(s => s.SeatId == SeatRules.Normalize(seatId));
        if (seat == null) return $"There is no seat \"{SeatRules.Normalize(seatId)}\".";
        string clean = SeatNames.Clean(name);
        if (clean.Length == 0) return "Enter a name.";
        seat.DisplayName = clean;
        return RosterCommit.Write(_store, _backups, Folder, _keep, $"rename {seat.SeatId}", null, party);
    }

    // ---- backups

    public IReadOnlyList<BackupInfo> Backups() => _backups.List(Folder);

    /// <summary>Returns null and the name of the backup that undoes it, or an error.</summary>
    public string? RestoreBackup(string name, out string? undo)
    {
        undo = null;
        try
        {
            undo = _backups.Restore(Folder, name, _keep);
            return null;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    string? RunEdit(string reason, string seatId, Func<SerializableRun, PartyFile, RosterResult> edit)
    {
        SerializableRun? run;
        try { run = LoadRun(); }
        catch (InvalidDataException e) { return e.Message; }
        if (run == null) return "There is no run yet, so there is nothing to bench or restore.";
        var party = LoadParty();
        return Commit(reason, run, party, seatId, edit(run, party));
    }

    string? Commit(string reason, SerializableRun run, PartyFile party, string seatId, RosterResult result) =>
        result.Ok ? RosterCommit.Write(_store, _backups, Folder, _keep, reason, run, party, SeatRules.Normalize(seatId), result) : result.Refusal;
}
