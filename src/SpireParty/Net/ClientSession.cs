using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Blitz;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.Dread;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using SpireParty.Core;
using SpireParty.Platform;
using SpireParty.UI;

namespace SpireParty.Net;

/// <summary>
/// A SpireParty client: hello on the control channel, then the game's own JoinFlow over ENet
/// with the seat's player ID as the net ID. Ends when the game's net service disconnects.
/// </summary>
public sealed class ClientSession
{
    public static ClientSession? Current { get; private set; }

    readonly ControlClient _control;
    INetGameService? _netService;

    ClientSession(ControlClient control) => _control = control;

    /// <summary>Parses "host" or "host:port".</summary>
    public static bool TryParseAddress(string input, int defaultPort, out string host, out int port)
    {
        host = input.Trim();
        port = defaultPort;
        int colon = host.LastIndexOf(':');
        if (colon > 0 && host.IndexOf(':') == colon)   // one colon: host:port (IPv6 literals not supported here)
        {
            if (!int.TryParse(host[(colon + 1)..], out port) || port is < 1 or > 65535) return false;
            host = host[..colon];
        }
        return host.Length > 0;
    }

    /// <summary>Returns an error to show in the Join form, or null once the join is under way.</summary>
    public static async Task<string?> StartAsync(string address, string seatId, string token, NSubmenuStack stack, string name = "")
    {
        if (Current != null || SpirePartySession.IsActive) return "A SpireParty session is already running.";

        string dataDir = PlatformInfo.SpirePartyDataDir;
        var config = MachineConfig.Load(dataDir, out _);
        if (!TryParseAddress(address, config.GamePort, out string host, out int port))
            return "Enter the host's address, for example 100.101.102.103 or my-pc, optionally with :port.";

        var release = ReleaseInfoManager.Instance.ReleaseInfo;
        var hello = new Hello(ModEntry.Version, release?.Version ?? "unknown", release?.Commit ?? "unknown",
            SeatRules.Normalize(seatId), SeatTokens.Normalize(token), SeatNames.Clean(name));

        var result = await ControlClient.ConnectAsync(host, port, hello);
        if (result.Client == null)
            return result.Reply is Refuse refuse ? refuse.Reason : result.Error ?? "The host did not answer.";

        var welcome = result.Client.Welcome;
        config.Remember(new RecentHost(host, port, welcome.SeatId, SeatNames.Clean(name)));
        try { config.Save(dataDir); } catch (Exception e) { Log.Warn($"{ModEntry.LogTag} could not save config.json: {e.Message}"); }

        var session = new ClientSession(result.Client);
        Current = session;
        SpirePartySession.Begin(new SessionContext(SessionRole.Client, welcome.PartyName, null, welcome.SeatId, welcome.PlayerId));
        SeatNames.Set(welcome.Names ?? new System.Collections.Generic.Dictionary<ulong, string>());
        result.Client.RosterReceived += roster => SeatNames.Set(roster.Names);
        PartyUnlocks.Set(PartyUnlocks.ForClient(welcome.Unlocks, welcome.HostUnlocks, welcome.HostMaxStandardAscension, welcome.HostMaxBlitzAscension));
        PartyGameplay.Set(welcome.EnemyHealthMultiplier);
        Log.Info($"{ModEntry.LogTag} enemy health multiplier {PartyGameplay.EnemyHealthMultiplier} (from the host)");
        Log.Info($"{ModEntry.LogTag} unlocks: {PartyUnlocks.Current!.Mode}, max ascension {PartyUnlocks.Current.MaxStandardAscension} (Blitz {PartyUnlocks.Current.MaxBlitzAscension})");
        result.Client.HostLost += reason => MainThread.Post(() => Log.Info($"{ModEntry.LogTag} control channel to host closed: {reason}"));
        Log.Info($"{ModEntry.LogTag} joined party \"{welcome.PartyName}\" as seat \"{welcome.SeatId}\" (player {welcome.PlayerId}); connecting to game on {host}:{port}");

        await session.JoinGame(host, port, welcome, stack);
        return null;
    }

    async Task JoinGame(string host, int port, Welcome welcome, NSubmenuStack stack)
    {
        var joinFlow = new JoinFlow(new NetClientGameService(PeerVersionInfo.LocalDefault()),
            new ENetClientConnectionInitializer(welcome.PlayerId, host, (ushort)port));
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            JoinResult r = await joinFlow.Begin(tree);
            switch (r.sessionState)
            {
                case RunSessionState.InLobby when r.isCustom:
                    Push<NCustomRunScreen>(stack, s => s.InitializeMultiplayerAsClient(joinFlow.NetService, r.joinResponse!.Value));
                    break;
                case RunSessionState.InLobby when r.gameMode == GameMode.Daily:
                    Push<NDailyRunScreen>(stack, s => s.InitializeMultiplayerAsClient(joinFlow.NetService, r.joinResponse!.Value));
                    break;
                case RunSessionState.InLobby when r.gameMode == GameMode.Blitz:
                    Push<NBlitzScreen>(stack, s => s.InitializeMultiplayerAsClient(joinFlow.NetService, r.joinResponse!.Value));
                    break;
                case RunSessionState.InLobby when r.gameMode == GameMode.Dread:
                    Push<NDreadScreen>(stack, s => s.InitializeMultiplayerAsClient(joinFlow.NetService, r.joinResponse!.Value));
                    break;
                case RunSessionState.InLobby:
                    Push<NCharacterSelectScreen>(stack, s => s.InitializeMultiplayerAsClient(joinFlow.NetService, r.joinResponse!.Value));
                    break;
                case RunSessionState.InLoadedLobby when r.isCustom:
                    Push<NCustomRunLoadScreen>(stack, s => s.InitializeAsClient(joinFlow.NetService, r.loadJoinResponse!.Value));
                    break;
                case RunSessionState.InLoadedLobby when r.gameMode == GameMode.Daily:
                    Push<NDailyRunLoadScreen>(stack, s => s.InitializeAsClient(joinFlow.NetService, r.loadJoinResponse!.Value));
                    break;
                case RunSessionState.InLoadedLobby:
                    Push<NMultiplayerLoadGameScreen>(stack, s => s.InitializeAsClient(joinFlow.NetService, r.loadJoinResponse!.Value));
                    break;
                default:
                    // Live rejoin into a running run is not supported by the game (see docs/findings.md).
                    joinFlow.NetService.Disconnect(NetError.RunInProgress);
                    Popups.Message("Run already in progress",
                        "The party is in the middle of a run. Ask the host to choose Save and Quit and reopen the party, then join again.");
                    Stop();
                    return;
            }
            // Only now is the game connected; from here on, a disconnect ends the session.
            _netService = joinFlow.NetService;
            MainThread.EveryFrame += CheckStillRunning;
            if (welcome.NewToken != null && !Dev.DevSwitches.SaveTestToken(welcome.SeatId, welcome.NewToken)) ShowNewToken(welcome);
        }
        catch (OperationCanceledException)
        {
            Stop();
        }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} join failed: {e}");
            var info = e is ClientConnectionFailedException failed ? failed.info : new NetErrorInfo(NetError.InternalError, selfInitiated: false);
            var popup = NErrorPopup.Create(info);
            if (popup != null) NModalContainer.Instance?.Add(popup);
            try { joinFlow.NetService.Disconnect(info.GetReason()); } catch { }
            Stop();
        }
    }

    static void Push<T>(NSubmenuStack stack, Action<T> init) where T : NSubmenu
    {
        var screen = stack.GetSubmenuType<T>();
        init(screen);
        stack.Push(screen);
    }

    static void ShowNewToken(Welcome welcome)
    {
        var form = new Form("Your seat token");
        form.AddText($"You joined \"{welcome.PartyName}\" as seat \"{welcome.SeatId}\". " +
                     "To join this party again you need this token. Write it down; it is shown only once.");
        form.AddCopyableText(welcome.NewToken!);
        form.AddButtons(("Got it", form.Close));
        form.Show();
    }

    void CheckStillRunning()
    {
        // See HostSession.CheckStillRunning: the run's cleanup writes after the disconnect.
        if (Current == this && _netService != null && !_netService.IsConnected && !RunManager.Instance.IsInProgress) Stop();
    }

    public void Stop()
    {
        if (Current != this) return;
        MainThread.EveryFrame -= CheckStillRunning;
        Current = null;
        _control.Dispose();
        SpirePartySession.End();
        SeatNames.Clear();
        PartyUnlocks.Set(null);
        PartyGameplay.Reset();
        GameAdapters.ProfileGuardAdapter.RestoreProgressFromDisk();
        Log.Info($"{ModEntry.LogTag} client session ended");
    }
}
