using System;
using System.Collections.Generic;
using System.Linq;

namespace SpireParty.Core;

/// <summary>One roster edit from the dev switch <c>--spireparty-roster</c> (M3 task 3.10).</summary>
/// <param name="Arg">The seat ID, or for <c>backup</c> the backup name ("latest" = the newest).</param>
public sealed record RosterOp(string Party, string Op, string Arg, string Character);

/// <summary>
/// Parses <c>--spireparty-roster=&lt;party&gt;,&lt;op&gt;,&lt;seat&gt;[,&lt;character&gt;]</c>, several separated by ';'.
/// Ops: bench, restore, add (needs a character), reset (token), remove, backup (restore a backup: name or "latest").
/// Each edit goes through the roster editor, so it is backed up and refused with a reason exactly as in the menus.
/// </summary>
public static class RosterSwitch
{
    public static readonly string[] Ops = { "bench", "restore", "add", "reset", "remove", "backup" };

    public static IReadOnlyList<RosterOp> Parse(string text, out string? error)
    {
        error = null;
        var ops = new List<RosterOp>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var a = part.Split(',').Select(x => x.Trim()).ToArray();
            string op = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            if (a.Length < 3 || a[0].Length == 0 || a[2].Length == 0 || !Ops.Contains(op))
            {
                error = $"\"{part}\": expected <party>,<{string.Join("|", Ops)}>,<seat or backup>[,<character>]";
                return Array.Empty<RosterOp>();
            }
            string character = a.Length > 3 ? a[3] : "";
            if (op == "add" && character.Length == 0)
            {
                error = $"\"{part}\": add needs a character, e.g. {a[0]},add,{a[2]},silent";
                return Array.Empty<RosterOp>();
            }
            ops.Add(new RosterOp(a[0], op, a[2], character));
        }
        if (ops.Count == 0) error = "no roster edit given";
        return ops;
    }
}
