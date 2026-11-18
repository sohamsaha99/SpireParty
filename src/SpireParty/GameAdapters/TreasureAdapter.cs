using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;
using SpireParty.Core;

namespace SpireParty.GameAdapters;

/// <summary>
/// Treasure room relics for more players than the hand-made layouts (findings → M3, four-player audit
/// row 7). <c>NTreasureRoomRelicCollection.InitializeRelics</c> shows the layout whose index is
/// "relic count − 1" (children of <c>Container</c>) and uses its holders; with more relics than layouts
/// no holder is used and the room cannot be finished. One relic per player, two more in Blitz.
///
/// Prefix (session only): when relics outnumber the layouts, the largest layout gets extra holders
/// (new instances of its own holder scene), and the missing layout slots point at it, so the game picks
/// it. Postfix: more than four holders are laid out in centred rows of four around the first four.
/// The row layout follows RemoveMultiplayerPlayerLimit (src/Patches.Treasure.cs, CC0; see
/// THIRD_PARTY_NOTICES.md), whose code targets a different version of this screen; its "skip relic"
/// button is not taken (it changes how picks are settled).
/// </summary>
public sealed class TreasureAdapter : GameAdapter
{
    public override string Name => "Treasure";
    public override IReadOnlyList<string> Features => new[] { Features_.MoreThanFourPlayers };

    static readonly MethodInfo? Init = AccessTools.Method(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection.InitializeRelics));
    static readonly FieldInfo? LayoutsField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_holderLayouts");
    static readonly FieldInfo? InUseField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_holdersInUse");

    const int RowLength = 4;
    const float MinXStep = 190f, MinYStep = 120f, FallbackXStep = 220f;   // RMP's spacing
    static bool _loggedLayouts;

    public override string? SelfCheck()
    {
        if (Init == null) return "method NTreasureRoomRelicCollection.InitializeRelics not found";
        if (LayoutsField?.FieldType != typeof(List<Control>)) return "field NTreasureRoomRelicCollection._holderLayouts not found";
        if (InUseField?.FieldType != typeof(List<NTreasureRoomRelicHolder>)) return "field NTreasureRoomRelicCollection._holdersInUse not found";
        return null;
    }

    public override void Apply(Harmony harmony) =>
        harmony.Patch(Init, prefix: new HarmonyMethod(typeof(TreasureAdapter), nameof(Prefix)),
            postfix: new HarmonyMethod(typeof(TreasureAdapter), nameof(Postfix)));

    static void Prefix(NTreasureRoomRelicCollection __instance)
    {
        if (!SpirePartySession.IsActive) return;
        try
        {
            var relics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
            var layouts = (List<Control>)LayoutsField!.GetValue(__instance)!;
            if (!_loggedLayouts)
            {
                _loggedLayouts = true;
                Log.Info($"{ModEntry.LogTag} treasure: {layouts.Count} hand-made layouts, holders per layout " +
                         string.Join(",", layouts.Select(l => Holders(l).Count)));
            }
            if (relics == null || relics.Count <= layouts.Count || layouts.Count == 0) return;

            var largest = layouts.OrderBy(l => Holders(l).Count).Last();
            var holders = Holders(largest);
            if (holders.Count == 0) return;
            var template = holders[^1];
            var scene = string.IsNullOrEmpty(template.SceneFilePath) ? null : GD.Load<PackedScene>(template.SceneFilePath);
            for (int i = holders.Count; i < relics.Count; i++)
            {
                var holder = scene?.Instantiate<NTreasureRoomRelicHolder>() ?? template.Duplicate() as NTreasureRoomRelicHolder;
                if (holder == null) return;
                holder.Name = $"SpirePartyHolder{i + 1}";
                largest.AddChild(holder);
            }
            while (layouts.Count < relics.Count) layouts.Add(largest);
            Log.Info($"{ModEntry.LogTag} treasure: {relics.Count} relics, {Holders(largest).Count} holders in the largest layout");
        }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} treasure: could not add relic holders: {e}");
        }
    }

    static void Postfix(NTreasureRoomRelicCollection __instance)
    {
        if (!SpirePartySession.IsActive) return;
        try
        {
            var inUse = (List<NTreasureRoomRelicHolder>)InUseField!.GetValue(__instance)!;
            if (inUse.Count <= RowLength) return;
            // Rows of four, centred on where the first four holders were (RMP's layout).
            float minX = inUse.Take(RowLength).Min(h => h.Position.X), maxX = inUse.Take(RowLength).Max(h => h.Position.X);
            float minY = inUse.Take(RowLength).Min(h => h.Position.Y), maxY = inUse.Take(RowLength).Max(h => h.Position.Y);
            int rows = (int)Math.Ceiling(inUse.Count / (float)RowLength);
            float xStep = maxX > minX ? Math.Max(MinXStep, (maxX - minX) / (RowLength - 1)) : FallbackXStep;
            float yStep = Math.Max(MinYStep, maxY - minY);
            float cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            for (int r = 0, start = 0; r < rows; r++)
            {
                int n = Math.Min(RowLength, inUse.Count - start);
                float y = cy + (r - (rows - 1) / 2f) * yStep;
                float x0 = cx - (n - 1) * xStep / 2;
                for (int k = 0; k < n; k++) inUse[start + k].Position = new Vector2(x0 + k * xStep, y);
                start += n;
            }
        }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} treasure: could not lay out relic holders: {e}");
        }
    }

    static List<NTreasureRoomRelicHolder> Holders(Control layout) =>
        layout.GetChildren().OfType<NTreasureRoomRelicHolder>().ToList();
}
