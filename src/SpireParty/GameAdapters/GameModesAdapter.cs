using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Daily;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.Blitz;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.Dread;
using MegaCrit.Sts2.Core.Runs;
using SpireParty.Net;

namespace SpireParty.GameAdapters;

/// <summary>
/// Game modes in party runs (M3 task 3.9). The host opens each mode's own lobby screen (HostSession.PushLobby).
/// Custom, Blitz and Dread take the player cap as an argument; <c>NDailyRunScreen</c> builds its
/// <c>StartRunLobby</c> with a literal 4 (audit row 5), so a prefix on that constructor (the one with a
/// <c>TimeServerResult</c>, used only by the daily) raises it to the party's cap while SpireParty hosts.
/// The self-check also covers the screen methods HostSession calls for each mode.
/// </summary>
public sealed class GameModesAdapter : GameAdapter
{
    public override string Name => "GameModes";
    public override IReadOnlyList<string> Features => new[] { Features_.GameModes };

    static readonly Type[] DailyCtorArgs =
    {
        typeof(GameMode), typeof(bool), typeof(INetGameService), typeof(IStartRunLobbyListener), typeof(TimeServerResult), typeof(int),
    };

    static readonly Type[] HostArgs = { typeof(INetGameService), typeof(int) };

    public override string? SelfCheck()
    {
        if (AccessTools.Constructor(typeof(StartRunLobby), DailyCtorArgs) == null)
            return "constructor StartRunLobby(..., TimeServerResult, int) not found";
        return Require(typeof(NDailyRunScreen), nameof(NDailyRunScreen), nameof(NDailyRunScreen.InitializeMultiplayerAsHost), new[] { typeof(INetGameService) })
               ?? Require(typeof(NCustomRunScreen), nameof(NCustomRunScreen), nameof(NCustomRunScreen.InitializeMultiplayerAsHost), HostArgs)
               ?? Require(typeof(NBlitzScreen), nameof(NBlitzScreen), nameof(NBlitzScreen.InitializeMultiplayerAsHost), HostArgs)
               ?? Require(typeof(NDreadScreen), nameof(NDreadScreen), nameof(NDreadScreen.InitializeMultiplayerAsHost), HostArgs)
               ?? Require(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeMultiplayerAsHost), HostArgs);
    }

    public override void Apply(Harmony harmony) =>
        harmony.Patch(AccessTools.Constructor(typeof(StartRunLobby), DailyCtorArgs),
            prefix: new HarmonyMethod(typeof(GameModesAdapter), nameof(DailyLobbyPrefix)));

    static void DailyLobbyPrefix(GameMode gameMode, INetGameService netService, ref int maxPlayers)
    {
        if (HostSession.Current is not { } host || netService.Type != NetGameType.Host) return;
        if (gameMode != GameMode.Daily || maxPlayers == host.PlayerCap) return;
        Log.Info($"{ModEntry.LogTag} daily lobby: player cap {maxPlayers} -> {host.PlayerCap}");
        maxPlayers = host.PlayerCap;
    }
}
