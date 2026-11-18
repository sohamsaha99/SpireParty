using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpireParty.Core;

public sealed record BackupInfo(string Name, DateTime Time, string Reason, string Roster);

/// <summary>
/// Party backups (hard rule 10): <c>parties/&lt;party&gt;/backups/&lt;yyyyMMdd-HHmmss&gt;/</c> holds copies of
/// <c>run.save</c> (if any), <c>party.json</c> and <c>benched/</c>, plus <c>backup.json</c> (time, reason, roster).
/// Taken before every roster edit; the newest <c>backupsKept</c> are kept. Restoring backs up the current
/// state first, so a restore can be undone too.
/// </summary>
public sealed class PartyBackups
{
    const string MetaFile = "backup.json";
    readonly PartyStore _store;
    readonly Func<DateTime> _clock;

    public PartyBackups(PartyStore store, Func<DateTime>? clock = null)
    {
        _store = store;
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>Copies the party's current files into a new backup, prunes old ones, returns the backup's name.</summary>
    public string Take(string folder, string reason, int keep)
    {
        var now = _clock();
        string dir = Path.Combine(_store.BackupsDir(folder), now.ToString("yyyyMMdd-HHmmss"));
        for (int n = 2; Directory.Exists(dir); n++)
            dir = Path.Combine(_store.BackupsDir(folder), $"{now:yyyyMMdd-HHmmss}-{n}");
        string tmp = dir + ".tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Directory.CreateDirectory(tmp);

        CopyIfExists(_store.RunSavePath(folder), Path.Combine(tmp, PartyStore.RunSaveFileName));
        CopyIfExists(_store.PartyFilePath(folder), Path.Combine(tmp, PartyStore.PartyFileName));
        CopyDir(_store.BenchedDir(folder), Path.Combine(tmp, "benched"));

        var meta = new JsonObject
        {
            ["time"] = now.ToString("o"),
            ["reason"] = reason,
            ["roster"] = RosterSummary(_store.PartyFilePath(folder)),
        };
        File.WriteAllText(Path.Combine(tmp, MetaFile), meta.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Directory.Move(tmp, dir); // a backup appears complete or not at all

        Prune(folder, keep);
        return Path.GetFileName(dir);
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<BackupInfo> List(string folder)
    {
        string root = _store.BackupsDir(folder);
        if (!Directory.Exists(root)) return Array.Empty<BackupInfo>();
        return Directory.GetDirectories(root)
            .Where(d => !d.EndsWith(".tmp", StringComparison.Ordinal))
            .Select(d => Read(d))
            .OrderByDescending(b => b.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Puts a backup's files back in place of the current ones, after backing up the current state.
    /// Returns the name of that new backup (restore it to undo).
    /// </summary>
    public string Restore(string folder, string name, int keep)
    {
        string src = Path.Combine(_store.BackupsDir(folder), name);
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..") || !Directory.Exists(src))
            throw new InvalidOperationException($"There is no backup \"{name}\".");

        // Read the backup into memory first: taking the safety backup below may prune it.
        byte[]? save = ReadIfExists(Path.Combine(src, PartyStore.RunSaveFileName));
        byte[]? party = ReadIfExists(Path.Combine(src, PartyStore.PartyFileName));
        var benched = ReadDir(Path.Combine(src, "benched"));
        if (party == null) throw new InvalidOperationException($"Backup \"{name}\" has no party.json.");

        string undo = Take(folder, $"before restoring {name}", keep);

        string savePath = _store.RunSavePath(folder);
        if (save != null) WriteAtomic(savePath, save);
        else
        {
            File.Delete(savePath);
            File.Delete(savePath + ".backup"); // otherwise the reader would fall back to it
        }
        WriteAtomic(_store.PartyFilePath(folder), party);

        string benchedDir = _store.BenchedDir(folder);
        if (Directory.Exists(benchedDir)) Directory.Delete(benchedDir, recursive: true);
        if (benched.Count > 0)
        {
            Directory.CreateDirectory(benchedDir);
            foreach (var (file, bytes) in benched) File.WriteAllBytes(Path.Combine(benchedDir, file), bytes);
        }
        return undo;
    }

    void Prune(string folder, int keep)
    {
        foreach (var old in List(folder).Skip(Math.Max(1, keep)))
            Directory.Delete(Path.Combine(_store.BackupsDir(folder), old.Name), recursive: true);
    }

    static BackupInfo Read(string dir)
    {
        string name = Path.GetFileName(dir);
        DateTime time = Directory.GetCreationTime(dir);
        string reason = "", roster = "";
        try
        {
            var j = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, MetaFile)));
            if (DateTime.TryParse((string?)j?["time"], null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)) time = t;
            reason = (string?)j?["reason"] ?? "";
            roster = (string?)j?["roster"] ?? "";
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException)
        {
            // A backup without readable metadata is still listed and restorable.
        }
        return new BackupInfo(name, time, reason, roster);
    }

    /// <summary>"host, tester, newbie (benched)": the seats that are not removed.</summary>
    static string RosterSummary(string partyFile)
    {
        if (!File.Exists(partyFile)) return "";
        var party = PartyFile.Parse(File.ReadAllText(partyFile), out _);
        return string.Join(", ", party.Seats
            .Where(s => s.State != SeatState.Empty)
            .Select(s => s.State == SeatState.Benched ? $"{s.SeatId} (benched)" : s.SeatId));
    }

    static void CopyIfExists(string from, string to)
    {
        if (File.Exists(from)) File.Copy(from, to);
    }

    static void CopyDir(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
    }

    static byte[]? ReadIfExists(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    static List<(string File, byte[] Bytes)> ReadDir(string dir) =>
        Directory.Exists(dir)
            ? Directory.GetFiles(dir).Select(f => (Path.GetFileName(f), File.ReadAllBytes(f))).ToList()
            : new List<(string, byte[])>();

    internal static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }
}
