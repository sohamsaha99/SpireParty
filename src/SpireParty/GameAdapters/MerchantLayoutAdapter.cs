using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SpireParty.Core;

namespace SpireParty.GameAdapters;

/// <summary>
/// Player characters in the shop for 5–8 players (findings → M3, four-player audit row 11).
/// <c>NMerchantRoom.AfterRoomIsLoaded</c> puts them in a √N grid that, for 5–8, piles up behind the
/// player panels. Postfix (session only, more than four): rows of four, moved toward the middle.
/// From RemoveMultiplayerPlayerLimit (src/Patches.Merchant.cs, CC0; see THIRD_PARTY_NOTICES.md),
/// with its constants; changed to session-only and rows of at most four.
/// </summary>
public sealed class MerchantLayoutAdapter : GameAdapter
{
    public override string Name => "MerchantLayout";
    // Cosmetic: without it, 5–8 players are crowded in the shop.
    public override IReadOnlyList<string> Features => Array.Empty<string>();

    const float ForwardX = 160f, ForwardY = 35f, RowStartX = -110f, RowStepY = -40f, ColumnStepX = -230f;
    const int RowLength = 4;

    public override string? SelfCheck() =>
        Require(typeof(NMerchantRoom), nameof(NMerchantRoom), "AfterRoomIsLoaded")
        ?? (AccessTools.PropertyGetter(typeof(NMerchantRoom), nameof(NMerchantRoom.PlayerVisuals)) == null
            ? "property NMerchantRoom.PlayerVisuals not found" : null);

    public override void Apply(Harmony harmony) =>
        harmony.Patch(AccessTools.Method(typeof(NMerchantRoom), "AfterRoomIsLoaded"),
            postfix: new HarmonyMethod(typeof(MerchantLayoutAdapter), nameof(Postfix)));

    static void Postfix(NMerchantRoom __instance)
    {
        if (!SpirePartySession.IsActive) return;
        try
        {
            var visuals = __instance.PlayerVisuals;
            if (visuals.Count <= RowLength) return;
            int rows = (int)Math.Ceiling(visuals.Count / (float)RowLength);
            int perRow = (int)Math.Ceiling(visuals.Count / (float)rows);
            int i = 0;
            for (int row = 0; row < rows; row++)
            {
                float x = ForwardX + RowStartX * row, y = ForwardY + RowStepY * row;
                for (int col = 0; col < perRow && i < visuals.Count; col++, i++)
                {
                    visuals[i].Position = new Vector2(x, y);
                    x += ColumnStepX;
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} shop layout failed (game layout kept): {e}");
        }
    }
}
