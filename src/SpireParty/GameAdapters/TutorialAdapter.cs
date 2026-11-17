using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Saves.Managers;
using SpireParty.Core;

namespace SpireParty.GameAdapters;

/// <summary>
/// During a SpireParty session every tutorial (FTUE) counts as already seen, so none are shown.
/// Without this, a fresh profile shows its tutorials in every party run: marking one "seen" is a
/// profile write, which <see cref="ProfileGuardAdapter"/> blocks (hard rule 1). This writes nothing;
/// outside a session tutorials behave as normal. Prefix on ProgressSaveManager.SeenFtue(string),
/// the check behind every tutorial (v0.113.1). One-time menu popups (SeenPopup) are left alone.
/// </summary>
public sealed class TutorialAdapter : GameAdapter
{
    public override string Name => "Tutorials";
    // Cosmetic: if this cannot be applied, tutorials simply show during party runs.
    public override IReadOnlyList<string> Features => Array.Empty<string>();

    public override string? SelfCheck() =>
        Require(typeof(ProgressSaveManager), nameof(ProgressSaveManager), nameof(ProgressSaveManager.SeenFtue), new[] { typeof(string) });

    public override void Apply(Harmony harmony) =>
        harmony.Patch(AccessTools.Method(typeof(ProgressSaveManager), nameof(ProgressSaveManager.SeenFtue), new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(TutorialAdapter), nameof(SeenFtuePrefix)));

    static bool SeenFtuePrefix(ref bool __result)
    {
        if (!SpirePartySession.IsActive) return true;
        __result = true;
        return false;
    }
}
