using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpireParty.Core;

/// <summary>
/// A folder of files to attach to a bug report after a state divergence in a party run (M3 task 3.12):
/// <c>user://mods/SpireParty/diagnostics/&lt;yyyyMMdd-HHmmss&gt;/</c>. Never inside a profile folder (hard rule 1).
/// Pure file work, no game types: the adapter collects the contents.
/// </summary>
public static class DiagnosticsBundle
{
    public const string FolderPattern = @"^\d{8}-\d{6}(-\d+)?$";
    static readonly Regex Folder = new(FolderPattern);

    /// <summary>
    /// Writes a new bundle and keeps only the newest <paramref name="keep"/> bundles. <paramref name="texts"/> are written
    /// as given; <paramref name="copies"/> (bundle file name → source path) are copied even while another program
    /// writes them (the game log). A copy that fails is noted in <c>missing.txt</c> instead. Returns the bundle folder.
    /// </summary>
    public static string Write(string diagnosticsDir, DateTime now, IReadOnlyDictionary<string, string> texts,
        IReadOnlyDictionary<string, string> copies, int keep)
    {
        Directory.CreateDirectory(diagnosticsDir);
        string name = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string dir = Path.Combine(diagnosticsDir, name);
        for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(diagnosticsDir, $"{name}-{n}");
        Directory.CreateDirectory(dir);

        foreach (var (file, text) in texts) File.WriteAllText(Path.Combine(dir, file), text);
        var missing = new List<string>();
        foreach (var (file, source) in copies)
        {
            try
            {
                using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var dst = File.Create(Path.Combine(dir, file));
                src.CopyTo(dst);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                missing.Add($"{file}: {source}: {e.Message}");
            }
        }
        if (missing.Count > 0) File.WriteAllLines(Path.Combine(dir, "missing.txt"), missing);

        Prune(diagnosticsDir, keep);
        return dir;
    }

    /// <summary>Deletes all but the newest <paramref name="keep"/> bundle folders. Other folders and files are left alone.</summary>
    public static void Prune(string diagnosticsDir, int keep)
    {
        if (!Directory.Exists(diagnosticsDir)) return;
        var bundles = Directory.GetDirectories(diagnosticsDir)
            .Where(d => Folder.IsMatch(Path.GetFileName(d)))
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal) // the names are timestamps
            .ToList();
        foreach (var old in bundles.Skip(Math.Max(1, keep)))
        {
            try { Directory.Delete(old, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* next time */ }
        }
    }

    /// <summary>
    /// party.json for a bug report: every seat's <c>tokenHash</c> removed. Returns null if the text is not a
    /// JSON object (it is then left out rather than attached as is).
    /// </summary>
    public static string? WithoutTokenHashes(string partyJson)
    {
        JsonObject? o;
        try { o = JsonNode.Parse(partyJson) as JsonObject; }
        catch (JsonException) { return null; }
        if (o == null) return null;
        if (o["seats"] is JsonArray seats)
            foreach (var seat in seats.OfType<JsonObject>())
                if (seat.ContainsKey("tokenHash")) seat["tokenHash"] = "(removed)";
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
