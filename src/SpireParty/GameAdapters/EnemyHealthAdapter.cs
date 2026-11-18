using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using SpireParty.Core;

namespace SpireParty.GameAdapters;

/// <summary>
/// Enemy health for party runs: postfix on <c>Creature.ScaleHpForMultiplayer</c> (base HP × players ×
/// act factor, findings → enemy scaling), the one place the game scales enemy HP for co-op; monsters
/// and events that scale HP themselves call it too. The factor is the party's enemyHealthMultiplier,
/// the same on every peer (<see cref="PartyGameplay"/>: host from party.json, clients from the welcome).
/// A test run can override it with
/// <c>--spireparty-test-enemyhp</c>, passed with the same value to every test process, so every peer
/// computes the same HP.
/// Outside a session, or without a factor, the game's value is unchanged.
/// </summary>
public sealed class EnemyHealthAdapter : GameAdapter
{
    public override string Name => "EnemyHealth";
    // If this cannot be applied, every peer (same build) uses the game's HP: the setting is ignored, no divergence.
    public override IReadOnlyList<string> Features => Array.Empty<string>();

    static readonly Type[] Args = { typeof(decimal), typeof(EncounterModel), typeof(int), typeof(int) };

    public override string? SelfCheck() =>
        Require(typeof(Creature), nameof(Creature), nameof(Creature.ScaleHpForMultiplayer), Args);

    public override void Apply(Harmony harmony) =>
        harmony.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.ScaleHpForMultiplayer), Args),
            postfix: new HarmonyMethod(typeof(EnemyHealthAdapter), nameof(Postfix)));

    /// <summary>The factor in force: 1 outside a session.</summary>
    public static decimal Factor =>
        !SpirePartySession.IsActive ? 1m : Dev.DevSwitches.TestEnemyHp ?? PartyGameplay.EnemyHealthMultiplier;

    static void Postfix(int playerCount, ref decimal __result)
    {
        if (playerCount <= 1) return;
        decimal factor = Factor;
        if (factor == 1m) return;
        decimal scaled = PartyGameplay.ScaleHp(__result, factor, playerCount);
        Log.Info($"{ModEntry.LogTag} enemy health x{factor}: {__result} -> {scaled} ({playerCount} players)");
        __result = scaled;
    }
}
