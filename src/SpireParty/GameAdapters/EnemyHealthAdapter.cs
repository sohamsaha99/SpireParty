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
/// and events that scale HP themselves call it too. M3 task 3.1/3.3 adds only the test-only factor
/// (<c>--spireparty-test-enemyhp</c>, passed with the same value to every test process, so every peer
/// computes the same HP). Task 3.5 adds the party's enemyHealthMultiplier, sent by the host.
/// Outside a session, or without a factor, the game's value is unchanged.
/// </summary>
public sealed class EnemyHealthAdapter : GameAdapter
{
    public override string Name => "EnemyHealth";
    // Only the test factor so far: nothing user-facing depends on it yet.
    public override IReadOnlyList<string> Features => Array.Empty<string>();

    static readonly Type[] Args = { typeof(decimal), typeof(EncounterModel), typeof(int), typeof(int) };
    static bool _logged;

    public override string? SelfCheck() =>
        Require(typeof(Creature), nameof(Creature), nameof(Creature.ScaleHpForMultiplayer), Args);

    public override void Apply(Harmony harmony) =>
        harmony.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.ScaleHpForMultiplayer), Args),
            postfix: new HarmonyMethod(typeof(EnemyHealthAdapter), nameof(Postfix)));

    /// <summary>The factor in force: 1 outside a session.</summary>
    public static decimal Factor => SpirePartySession.IsActive && Dev.DevSwitches.TestEnemyHp is decimal f ? f : 1m;

    static void Postfix(int playerCount, ref decimal __result)
    {
        if (playerCount <= 1) return;
        decimal factor = Factor;
        if (factor == 1m) return;
        decimal scaled = Math.Max(1m, Math.Ceiling(__result * factor));
        if (!_logged)
        {
            _logged = true;
            Log.Info($"{ModEntry.LogTag} enemy health x{factor}: {__result} -> {scaled} ({playerCount} players)");
        }
        __result = scaled;
    }
}
