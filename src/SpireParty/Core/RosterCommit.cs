using System;
using System.IO;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SpireParty.Core;

/// <summary>
/// Writes a roster edit that <see cref="RosterEdit"/> made in memory (hard rule 10):
/// back up the files on disk, write <c>run.save</c>, <c>party.json</c> and the seat's benched file,
/// and if any write fails, put the backup back. Never called during a live session.
/// </summary>
public static class RosterCommit
{
    public static SerializablePlayer ReadBenched(PartyStore store, string folder, string seatId)
    {
        string path = store.BenchedPath(folder, seatId);
        if (!File.Exists(path)) throw new InvalidOperationException($"The benched file for \"{seatId}\" is missing ({path}).");
        return PartyRunSave.PlayerFromBytes(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Returns null on success, or a readable reason. <paramref name="run"/> is null for edits that only
    /// change party.json (no run in progress). <paramref name="seatId"/> and <paramref name="result"/> say
    /// which benched file to write or delete.
    /// </summary>
    public static string? Write(PartyStore store, PartyBackups backups, string folder, int keep, string reason,
                                SerializableRun? run, PartyFile party, string? seatId = null, RosterResult? result = null)
    {
        string backup;
        try
        {
            backup = backups.Take(folder, reason, keep);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Could not back up the party first, so nothing was changed: {e.Message}";
        }

        try
        {
            if (run != null)
            {
                string savePath = store.RunSavePath(folder);
                PartyBackups.WriteAtomic(savePath, PartyRunSave.ToBytes(run));
                // The game's rolling .backup now holds the pre-edit run; drop it so a read never falls back to a
                // run that no longer matches party.json. The pre-edit run is in the backup just taken.
                File.Delete(savePath + ".backup");
            }
            store.Save(folder, party);
            if (seatId != null && result?.BenchedPlayer is { } benched)
                PartyBackups.WriteAtomic(store.BenchedPath(folder, seatId), PartyRunSave.PlayerToBytes(benched));
            if (seatId != null && result?.DeleteBenchedFile == true)
                File.Delete(store.BenchedPath(folder, seatId));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try
            {
                backups.Restore(folder, backup, keep + 1);
                return $"Saving the change failed, so the party was put back as it was: {e.Message}";
            }
            catch (Exception e2) when (e2 is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return $"Saving the change failed ({e.Message}) and putting the party back also failed ({e2.Message}). " +
                       $"Restore backup \"{backup}\" from the Backups screen.";
            }
        }
    }
}
