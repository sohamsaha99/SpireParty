using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SpireParty.Core;

namespace SpireParty.GameAdapters;

/// <summary>
/// Rest site seats for players 5–8 (findings → M3, four-player audit row 6). <c>NRestSiteRoom._Ready</c>
/// fills a list with the scene's four seats (<c>BgContainer/Character_1..4</c>), then indexes it once per
/// player, which throws for a fifth. A transpiler swaps that one <c>List&lt;Control&gt;</c> indexer for
/// <see cref="Seat"/>, which adds seats on demand during a session.
///
/// Adapted from RemoveMultiplayerPlayerLimit (Rain156/sts2-RMP-Mods, src/Patches.RestSite.cs, CC0; see
/// THIRD_PARTY_NOTICES.md): the indexer swap, the seat offsets and the copied logs. Changed: only in a
/// SpireParty session, no index wrap-around (8 seats is the cap), and its hover/select patches are left
/// out because this build already finds rest-site characters by net ID.
/// </summary>
public sealed class RestSiteAdapter : GameAdapter
{
    public override string Name => "RestSite";
    public override IReadOnlyList<string> Features => new[] { Features_.MoreThanFourPlayers };

    static readonly MethodInfo? ReadyMethod = AccessTools.Method(typeof(NRestSiteRoom), nameof(NRestSiteRoom._Ready));
    static readonly MethodInfo? Indexer = AccessTools.PropertyGetter(typeof(List<Control>), "Item");
    static readonly MethodInfo SeatMethod = AccessTools.Method(typeof(RestSiteAdapter), nameof(Seat));

    // Seats 5–8 sit beside the vanilla four: 5 and 6 left and right of the front pair, 7 and 8 beside the
    // back pair (RMP's offsets; seats 0 and 2 face right, 1 and 3 face left, and the game flips odd seats).
    static readonly Vector2 LeftFront = new(-250f, 35f), RightFront = new(250f, 35f);
    static readonly Vector2 LeftBack = new(-240f, -20f), RightBack = new(240f, -20f);
    static readonly Vector2 LogLeft = new(-250f, 0f), LogRight = new(250f, 0f);

    static int IndexerCalls(IEnumerable<CodeInstruction> code) => code.Count(c => Indexer != null && c.Calls(Indexer));

    public override string? SelfCheck()
    {
        if (ReadyMethod == null) return "method NRestSiteRoom._Ready not found";
        if (Indexer == null) return "List<Control> indexer not found";
        int n = IndexerCalls(PatchProcessor.GetOriginalInstructions(ReadyMethod));
        return n == 1 ? null : $"NRestSiteRoom._Ready: expected one seat lookup, found {n}";
    }

    public override void Apply(Harmony harmony) =>
        harmony.Patch(ReadyMethod, transpiler: new HarmonyMethod(typeof(RestSiteAdapter), nameof(Transpiler)));

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var ins in instructions)
        {
            if (Indexer != null && ins.Calls(Indexer))
                yield return new CodeInstruction(OpCodes.Call, SeatMethod) { labels = ins.labels, blocks = ins.blocks };
            else
                yield return ins;
        }
    }

    /// <summary>Same as <c>seats[index]</c>, but in a session adds the missing seats first.</summary>
    public static Control Seat(List<Control> seats, int index)
    {
        if (index >= seats.Count && SpirePartySession.IsActive && seats.Count >= 4 && index < WireWidths.MaxPlayers)
        {
            try { AddSeats(seats, index + 1); }
            catch (Exception e) { Log.Error($"{ModEntry.LogTag} rest site: could not add seat {index + 1}: {e}"); }
        }
        return seats[index];
    }

    static void AddSeats(List<Control> seats, int count)
    {
        if (seats[0].GetParent() is not Control parent) return;
        AddLogs(parent);
        while (seats.Count < count)
        {
            int i = seats.Count;          // 4..7
            int extra = i - 4;            // 0: left front, 1: right front, 2: left back, 3: right back
            bool left = extra % 2 == 0;
            Vector2 pos = extra < 2
                ? (left ? seats[0].Position + LeftFront : seats[1].Position + RightFront)
                : (left ? seats[2].Position + LeftBack : seats[3].Position + RightBack);
            var seat = seats[extra % 4].Duplicate() as Control ?? new Control();
            foreach (var child in seat.GetChildren()) { seat.RemoveChild(child); child.QueueFree(); }
            seat.Name = $"Character_{i + 1}";
            seat.Position = pos;
            parent.AddChild(seat);
            seats.Add(seat);
        }
    }

    /// <summary>Copies of the background's logs, shifted out, so the extra seats sit on something.</summary>
    static void AddLogs(Control parent)
    {
        if (parent.GetChildCount() == 0 || parent.GetChild(0) is not Node background) return;
        if (background.GetNodeOrNull("SpirePartyLogs") != null) return;
        background.AddChild(new Node { Name = "SpirePartyLogs" });
        foreach (var (path, offset) in new[] { ("RestSiteLLog", LogLeft), ("RestSiteRLog", LogRight),
                     ("RestSiteLighting/RestSiteLLog2", LogLeft), ("RestSiteLighting/RestSiteRLog2", LogRight) })
        {
            var node = background.GetNodeOrNull(path);
            if (node?.GetParent() is not Node owner) { Log.Info($"{ModEntry.LogTag} rest site: no {path} to copy"); continue; }
            var copy = node.Duplicate();
            copy.Name = node.Name + "_SpireParty";
            owner.AddChild(copy);
            if (node is Control c && copy is Control cc) cc.Position = c.Position + offset;
            if (node is Node2D n2 && copy is Node2D cc2) cc2.Position = n2.Position + offset;
        }
    }
}
