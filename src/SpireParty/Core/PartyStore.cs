using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpireParty.Core;

/// <summary>
/// Party folders under SpireParty's data dir (<c>user://mods/SpireParty/parties/</c>).
/// The base directory is passed in (from <c>Platform/</c>), so this class is testable on a temp dir.
/// Never touches the game's profile folders (hard rule 1).
/// </summary>
public sealed class PartyStore
{
    public const string PartyFileName = "party.json";
    public const string RunSaveFileName = "run.save";
    public const string HostSeatId = "host";

    readonly string _partiesDir;
    public List<string> Warnings { get; } = new();

    public PartyStore(string spirePartyDataDir)
    {
        _partiesDir = Path.Combine(spirePartyDataDir, "parties");
        DeletedDir = Path.Combine(spirePartyDataDir, "deleted");
    }

    /// <summary>Deleted parties, <c>deleted/&lt;folder&gt;-&lt;yyyyMMdd-HHmmss&gt;</c>, newest kept (config <c>deletedPartiesKept</c>).</summary>
    public string DeletedDir { get; }

    static readonly Regex DeletedName = new(@"-(\d{8}-\d{6})(?:-(\d+))?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Deletes a party the safe way: its whole folder (party.json, run save, backups, benched players, history) moves
    /// to <see cref="DeletedDir"/>, then only the newest <paramref name="keep"/> deleted parties are kept. Returns the
    /// folder it moved to. Throws if the party does not exist. Never touches the game's profiles.
    /// </summary>
    public string Delete(string folder, DateTime now, int keep)
    {
        string source = PartyDir(folder);
        if (!Directory.Exists(source)) throw new InvalidOperationException($"There is no party called \"{folder}\".");
        Directory.CreateDirectory(DeletedDir);
        string stamp = now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string target = Path.Combine(DeletedDir, $"{folder}-{stamp}");
        for (int n = 2; Directory.Exists(target); n++) target = Path.Combine(DeletedDir, $"{folder}-{stamp}-{n}");
        Directory.Move(source, target);
        PruneDeleted(keep);
        return target;
    }

    /// <summary>The deleted parties, newest first (by the time in the folder name).</summary>
    public IReadOnlyList<string> ListDeleted()
    {
        if (!Directory.Exists(DeletedDir)) return Array.Empty<string>();
        return Directory.GetDirectories(DeletedDir)
            .Select(d => (Path: d, Match: DeletedName.Match(Path.GetFileName(d))))
            .Where(x => x.Match.Success)
            .OrderByDescending(x => x.Match.Groups[1].Value, StringComparer.Ordinal)
            .ThenByDescending(x => x.Match.Groups[2].Success ? int.Parse(x.Match.Groups[2].Value) : 1)
            .Select(x => x.Path)
            .ToList();
    }

    /// <summary>Removes all but the newest <paramref name="keep"/> (at least 1) deleted parties. Other files are left alone.</summary>
    public void PruneDeleted(int keep)
    {
        foreach (var old in ListDeleted().Skip(Math.Max(1, keep)))
        {
            try { Directory.Delete(old, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Warnings.Add($"could not remove {old}: {e.Message}"); }
        }
    }

    public string PartyDir(string folder) => Path.Combine(_partiesDir, folder);
    public string RunSavePath(string folder) => Path.Combine(PartyDir(folder), RunSaveFileName);
    public string HistoryDir(string folder) => Path.Combine(PartyDir(folder), "history");
    public string PartyFilePath(string folder) => Path.Combine(PartyDir(folder), PartyFileName);
    public string BenchedDir(string folder) => Path.Combine(PartyDir(folder), "benched");
    /// <summary>Seat IDs are validated lower-case names (<see cref="SeatRules.IsValidSeatId"/>), safe as file names.</summary>
    public string BenchedPath(string folder, string seatId) => Path.Combine(BenchedDir(folder), SeatRules.Normalize(seatId) + ".json");
    public string BackupsDir(string folder) => Path.Combine(PartyDir(folder), "backups");

    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_partiesDir)) return Array.Empty<string>();
        return Directory.GetDirectories(_partiesDir)
            .Select(Path.GetFileName)
            .Where(n => n != null && File.Exists(Path.Combine(_partiesDir, n, PartyFileName)))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Creates a party with the host seat. Throws if the name is unusable or taken.</summary>
    public (string Folder, PartyFile Party) Create(string displayName, string mode = PartyModes.Standard)
    {
        string folder = SeatRules.PartyFolderName(displayName);
        if (folder.Length == 0) throw new ArgumentException("The party name needs at least one letter or digit.");
        if (Directory.Exists(PartyDir(folder))) throw new InvalidOperationException($"A party called \"{folder}\" already exists.");

        var party = new PartyFile { Name = displayName.Trim(), Mode = PartyModes.Parse(mode) ?? PartyModes.Standard };
        party.Seats.Add(new Seat
        {
            SeatId = HostSeatId,
            PlayerId = SeatRules.HostPlayerId,
            DisplayName = "Host",
        });
        Directory.CreateDirectory(PartyDir(folder));
        Save(folder, party);
        return (folder, party);
    }

    public PartyFile Load(string folder)
    {
        string path = Path.Combine(PartyDir(folder), PartyFileName);
        string? json = File.Exists(path) ? File.ReadAllText(path) : null;
        var party = PartyFile.Parse(json, out var warnings);
        Warnings.AddRange(warnings.Select(w => $"{folder}: {w}"));
        if (party.Seats.All(s => s.PlayerId != SeatRules.HostPlayerId))
            party.Seats.Insert(0, new Seat { SeatId = HostSeatId, PlayerId = SeatRules.HostPlayerId, DisplayName = "Host" });
        return party;
    }

    /// <summary>Writes atomically: temp file, then replace, so a crash never leaves half a file.</summary>
    public void Save(string folder, PartyFile party)
    {
        string path = Path.Combine(PartyDir(folder), PartyFileName);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, party.ToJson());
        File.Move(tmp, path, overwrite: true);
    }
}
