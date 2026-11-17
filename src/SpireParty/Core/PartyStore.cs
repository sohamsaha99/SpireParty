using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
    }

    public string PartyDir(string folder) => Path.Combine(_partiesDir, folder);
    public string RunSavePath(string folder) => Path.Combine(PartyDir(folder), RunSaveFileName);
    public string HistoryDir(string folder) => Path.Combine(PartyDir(folder), "history");

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
    public (string Folder, PartyFile Party) Create(string displayName)
    {
        string folder = SeatRules.PartyFolderName(displayName);
        if (folder.Length == 0) throw new ArgumentException("The party name needs at least one letter or digit.");
        if (Directory.Exists(PartyDir(folder))) throw new InvalidOperationException($"A party called \"{folder}\" already exists.");

        var party = new PartyFile { Name = displayName.Trim() };
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
