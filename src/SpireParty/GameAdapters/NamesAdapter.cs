using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Null;
using MegaCrit.Sts2.Core.Platform.Steam;
using SpireParty.Core;

namespace SpireParty.GameAdapters;

/// <summary>
/// During a SpireParty session, players are shown by their seat display names (<see cref="SeatNames"/>)
/// instead of player IDs ("7000002") or Steam names. Every name in the game (lobby, nameplates, powers,
/// rest site, game over) comes from <c>PlatformUtil.GetPlayerNameRaw</c> (v0.113.1). That method is tiny
/// and may be inlined by the JIT, so the strategies it calls are patched too. Prefixes; IDs not in the
/// table, and everything outside a session, fall through to the game.
/// </summary>
public sealed class NamesAdapter : GameAdapter
{
    public override string Name => "Names";
    // Cosmetic: if this cannot be applied, the game shows player IDs as in M1.
    public override IReadOnlyList<string> Features => Array.Empty<string>();

    static readonly Type[] RawArgs = { typeof(PlatformType), typeof(ulong) };
    static readonly Type[] IdArg = { typeof(ulong) };

    public override string? SelfCheck() =>
        Require(typeof(PlatformUtil), nameof(PlatformUtil), nameof(PlatformUtil.GetPlayerNameRaw), RawArgs)
        ?? Require(typeof(PlatformUtil), nameof(PlatformUtil), nameof(PlatformUtil.GetPlayerName), RawArgs)
        ?? Require(typeof(NullPlatformUtilStrategy), nameof(NullPlatformUtilStrategy), "GetPlayerName", IdArg)
        ?? Require(typeof(SteamPlatformUtilStrategy), nameof(SteamPlatformUtilStrategy), "GetPlayerName", IdArg);

    public override void Apply(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(PlatformUtil), nameof(PlatformUtil.GetPlayerNameRaw), RawArgs),
            prefix: new HarmonyMethod(typeof(NamesAdapter), nameof(RawPrefix)));
        harmony.Patch(AccessTools.Method(typeof(PlatformUtil), nameof(PlatformUtil.GetPlayerName), RawArgs),
            prefix: new HarmonyMethod(typeof(NamesAdapter), nameof(EscapedPrefix)));
        harmony.Patch(AccessTools.Method(typeof(NullPlatformUtilStrategy), "GetPlayerName", IdArg),
            prefix: new HarmonyMethod(typeof(NamesAdapter), nameof(StrategyPrefix)));
        harmony.Patch(AccessTools.Method(typeof(SteamPlatformUtilStrategy), "GetPlayerName", IdArg),
            prefix: new HarmonyMethod(typeof(NamesAdapter), nameof(StrategyPrefix)));
    }

    static bool TryName(ulong playerId, out string name)
    {
        name = "";
        return SpirePartySession.IsActive && SeatNames.TryGet(playerId, out name);
    }

    static bool RawPrefix(ulong playerId, ref string __result)
    {
        if (!TryName(playerId, out var name)) return true;
        __result = name;
        return false;
    }

    /// <summary>For BBCode labels: <see cref="SeatNames.Clean"/> already removes '[' and ']'.</summary>
    static bool EscapedPrefix(ulong playerId, ref string __result) => RawPrefix(playerId, ref __result);

    static bool StrategyPrefix(ulong playerId, ref string __result) => RawPrefix(playerId, ref __result);
}
