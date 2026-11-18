using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SpireParty.Core;

namespace SpireParty.GameAdapters;

/// <summary>
/// Player placement in combat for 5–8 players (findings → M3, four-player audit row 10).
/// <c>NCombatRoom.PositionPlayersAndPets</c> lays players out in a √N grid: three rows of three for
/// eight, which piles them on top of each other. Postfix (session only, more than four players): two
/// rows of up to four, the local player first in the front row, the back row shifted outward, raised
/// and dimmed as the game dims its back rows. Each player's pets move by the same offset as the player.
/// Spacing follows the game's own rules (70 px gap, at least 150 px from the centre, squeezed to fit
/// 960 / scaling). Players are also shrunk (85% for 5–6, 75% for 7–8) with NCreature.SetScaleAndHue,
/// which moves their hitboxes too; pets keep their size (the game resizes Osty itself).
/// </summary>
public sealed class CombatLayoutAdapter : GameAdapter
{
    public override string Name => "CombatLayout";
    // Cosmetic: without it, 5–8 players are crowded but playable.
    public override IReadOnlyList<string> Features => Array.Empty<string>();

    static readonly Type[] Args = { typeof(List<NCreature>), typeof(float), typeof(bool) };
    const float Gap = 70f, Margin = 150f, BackRowRaise = 110f, FrontY = 200f;
    static readonly Color Dim = new(0.5f, 0.5f, 0.5f), Lit = new(1f, 1f, 1f);

    public override string? SelfCheck() =>
        Require(typeof(NCombatRoom), nameof(NCombatRoom), nameof(NCombatRoom.PositionPlayersAndPets), Args);

    public override void Apply(Harmony harmony) =>
        harmony.Patch(AccessTools.Method(typeof(NCombatRoom), nameof(NCombatRoom.PositionPlayersAndPets), Args),
            postfix: new HarmonyMethod(typeof(CombatLayoutAdapter), nameof(Postfix)));

    static void Postfix(List<NCreature> creatureNodes, float scaling, bool fullyCenterPlayers)
    {
        if (!SpirePartySession.IsActive || fullyCenterPlayers) return;
        try
        {
            // Same order as the game: the local player first, then the others in list order.
            var players = creatureNodes.Where(c => c.Entity.IsPlayer).ToList();
            if (players.Count <= 4) return;
            var me = players.FirstOrDefault(c => LocalContext.IsMe(c.Entity));
            if (me != null) { players.Remove(me); players.Insert(0, me); }
            var pets = creatureNodes.Where(c => !c.Entity.IsPlayer).ToList();

            float size = players.Count > 6 ? 0.75f : 0.85f;
            foreach (var p in players) Shrink(p, size);
            int perRow = (int)Math.Ceiling(players.Count / 2.0);
            float width = 960f / scaling;
            Place(players.Take(perRow).ToList(), pets, width, FrontY, 0f, Lit);
            float avg = players.Average(Width);
            // Back row: half a place outward, so its players show between the front row's.
            Place(players.Skip(perRow).ToList(), pets, width, FrontY - BackRowRaise, (avg + Gap) * 0.5f, Dim);
        }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} combat layout failed (game layout kept): {e}");
        }
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NCreature, object> Shrunk = new();

    /// <summary>Once per creature: its default scale times <paramref name="factor"/>, hitbox included.</summary>
    static void Shrink(NCreature c, float factor)
    {
        if (Shrunk.TryGetValue(c, out _)) return;
        Shrunk.Add(c, new object());
        c.SetScaleAndHue(c.Visuals.DefaultScale * factor, 0f);
    }

    /// <summary>On-screen width: the bounds are inside the scaled visuals.</summary>
    static float Width(NCreature c) => c.Visuals.Bounds.Size.X * c.Visuals.Scale.X;

    /// <summary>One row, from the centre outward (players stand at negative x, left of the enemies).</summary>
    static void Place(List<NCreature> row, List<NCreature> pets, float width, float y, float shift, Color tint)
    {
        if (row.Count == 0) return;
        float sum = row.Sum(Width);
        float gap = Gap;
        width -= shift;                      // the shifted row must still fit
        float start = Math.Max((width - (sum + gap * (row.Count - 1))) * 0.5f, Margin);
        if (start + sum + gap * (row.Count - 1) > width && row.Count > 1)
        {
            gap = (width - Margin - sum) / (row.Count - 1);   // squeeze, as the game does
            start = Margin;
        }
        float x = start + shift;
        foreach (var player in row)
        {
            float w = Width(player);
            var target = new Vector2(-x - w * 0.5f, y);
            var delta = target - player.Position;
            player.Position = target;
            player.Visuals.Modulate = tint;
            foreach (var pet in pets.Where(p => p.Entity.PetOwner == player.Entity.Player))
            {
                pet.Position += delta;
                pet.Visuals.Modulate = tint;
            }
            x += w + gap;
        }
    }
}
