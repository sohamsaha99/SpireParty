using System;
using System.IO;
using System.Linq;
using System.Net;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireParty.Core;
using SpireParty.Platform;
using SpireParty.UI;

namespace SpireParty.Net;

/// <summary>
/// A running SpireParty host: control channel + the game's ENet host + the party on disk.
/// Created from the SpireParty Host menu; ends when the game's net service disconnects
/// (back to the main menu, run over, or host quit). Main thread, except the hello handler.
/// </summary>
public sealed class HostSession
{
    /// <summary>Players this party allows: its maxPlayers, at most what the game can carry (RosterEdit.Cap).</summary>
    int PlayerCap => RosterEdit.Cap(_party);

    public static HostSession? Current { get; private set; }

    readonly PartyStore _store;
    readonly string _folder;
    readonly PartyFile _party;
    readonly object _partyLock = new();
    readonly FailedAttemptLimiter _limiter = new();
    readonly SerializableRun? _run;
    readonly HostIdentity _identity;

    public ControlServer Server { get; }
    public NetHostGameService NetService { get; }
    public string RunSavePath => _store.RunSavePath(_folder);
    public string HistoryDir => _store.HistoryDir(_folder);

    HostSession(PartyStore store, string folder, PartyFile party, SerializableRun? run, HostIdentity identity,
        ControlServer server, NetHostGameService netService)
    {
        _store = store;
        _folder = folder;
        _party = party;
        _run = run;
        _identity = identity;
        Server = server;
        NetService = netService;
    }

    /// <summary>Opens the party and pushes the right lobby. Returns an error to show, or null on success.</summary>
    public static string? Start(string folder, NSubmenuStack stack)
    {
        if (Current != null || SpirePartySession.IsActive) return "A SpireParty session is already running.";

        string dataDir = PlatformInfo.SpirePartyDataDir;
        var config = MachineConfig.Load(dataDir, out var configWarnings);
        foreach (var w in configWarnings) Log.Warn($"{ModEntry.LogTag} {w}");

        var store = new PartyStore(dataDir);
        var party = store.Load(folder);
        foreach (var w in store.Warnings) Log.Warn($"{ModEntry.LogTag} {w}");

        SerializableRun? run = null;
        string savePath = store.RunSavePath(folder);
        if (PartyRunSave.Exists(savePath))
        {
            var read = PartyRunSave.Read(savePath);
            if (!read.Success || read.SaveData == null)
                return $"The party's saved run could not be read ({read.Status}). It was not changed.\n{read.ErrorMessage}";
            try
            {
                run = RunManager.CanonicalizeSave(read.SaveData, SeatRules.HostPlayerId);
            }
            catch (Exception e)
            {
                return $"The party's saved run is not valid for this host: {e.Message}";
            }
        }

        var release = ReleaseInfoManager.Instance.ReleaseInfo;
        var identity = new HostIdentity(ModEntry.Version, release?.Version ?? "unknown", release?.Commit ?? "unknown");

        HostSession? session = null;
        var plan = ListenAddresses.Resolve(config.ListenAddress);
        var server = new ControlServer(plan, config.GamePort,
            (hello, from, connected) => session!.OnHello(hello, from, connected),
            log: m => Log.Info($"{ModEntry.LogTag} {m}"));
        try
        {
            server.Start();
        }
        catch (IOException e)
        {
            return $"{e.Message}\nIs another program (or another game) already using port {config.GamePort}?";
        }

        var netService = new NetHostGameService(PeerVersionInfo.LocalDefault());
        var error = netService.StartENetHost((ushort)config.GamePort, RosterEdit.Cap(party));
        if (error.HasValue)
        {
            server.Dispose();
            return $"Could not start the game host on UDP {config.GamePort}: {error.Value.GetReason()}.";
        }

        session = new HostSession(store, folder, party, run, identity, server, netService);
        server.SeatLost += (seat, reason) => MainThread.Post(() => session.OnSeatLost(seat, reason));
        Current = session;
        SeatNames.Set(SeatNames.Of(party));
        PartyUnlocks.Set(PartyUnlocks.ForHost(party));
        Log.Info($"{ModEntry.LogTag} unlocks: {PartyUnlocks.Current!.Mode}, max ascension {PartyUnlocks.Current.MaxStandardAscension} (Blitz {PartyUnlocks.Current.MaxBlitzAscension})");
        SpirePartySession.Begin(new SessionContext(SessionRole.Host, party.Name, store.PartyDir(folder),
            PartyStore.HostSeatId, SeatRules.HostPlayerId));
        MainThread.EveryFrame += session.CheckStillRunning;
        Log.Info($"{ModEntry.LogTag} hosting party \"{party.Name}\" ({folder}) on TCP+UDP {config.GamePort}, " +
                 $"control channel on {plan.Description}; saved run: {(run != null ? "yes" : "no")}");

        try
        {
            PushLobby(stack, netService, run, RosterEdit.Cap(party));
        }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} could not open the lobby: {e}");
            session.Stop();
            return "Could not open the lobby screen. See the game log.";
        }
        return null;
    }

    static void PushLobby(NSubmenuStack stack, NetHostGameService netService, SerializableRun? run, int playerCap)
    {
        if (run == null)
        {
            var screen = stack.GetSubmenuType<NCharacterSelectScreen>();
            screen.InitializeMultiplayerAsHost(netService, playerCap);
            stack.Push(screen);
        }
        else if (run.GameMode == GameMode.Daily)
        {
            var screen = stack.GetSubmenuType<NDailyRunLoadScreen>();
            screen.InitializeAsHost(netService, run);
            stack.Push(screen);
        }
        else if (run.IsCustomGame)
        {
            var screen = stack.GetSubmenuType<NCustomRunLoadScreen>();
            screen.InitializeAsHost(netService, run);
            stack.Push(screen);
        }
        else
        {
            var screen = stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
            screen.InitializeAsHost(netService, run);
            stack.Push(screen);
        }
    }

    /// <summary>Called on a background thread by the control server, one hello at a time.</summary>
    ControlMessage OnHello(Hello hello, IPAddress from, System.Collections.Generic.IReadOnlyCollection<string> connected)
    {
        lock (_partyLock)
        {
            // New seats only before a run starts; seats join a saved run only if they are in it (the roster
            // editor adds players to a run while no session is running).
            bool open = _run == null;
            var runIds = _run?.Players.Select(p => p.NetId).ToList();
            var decision = HostHandshake.Decide(_identity, hello.SpirePartyVersion, hello.GameVersion, hello.GameCommit,
                hello.SeatId, hello.Token, from.ToString(), _party, new LobbyState(open, PlayerCap, connected, runIds), _limiter);

            if (decision is Deny deny) return new Refuse(deny.Code, deny.Reason);
            var admit = (Admit)decision;

            // The player's own name for their seat, if they typed one.
            string oldName = admit.Seat.DisplayName;
            string newName = SeatNames.Clean(hello.Name);
            bool renamed = newName.Length > 0 && newName != oldName;
            if (renamed) admit.Seat.DisplayName = newName;

            if (admit.NewToken != null || renamed)
            {
                try { _store.Save(_folder, _party); }
                catch (Exception e)
                {
                    if (admit.Created) _party.Seats.Remove(admit.Seat);
                    else
                    {
                        if (admit.NewToken != null) admit.Seat.TokenHash = ""; // stays open
                        admit.Seat.DisplayName = oldName;
                    }
                    Log.Error($"{ModEntry.LogTag} could not save party.json: {e}");
                    return new Refuse("host_error", "The host could not save your seat. Try again.");
                }
            }
            if (renamed) Log.Info($"{ModEntry.LogTag} seat \"{admit.Seat.SeatId}\" is now called \"{newName}\"");

            // Everyone already connected learns the new seat's name or the new name; the joiner gets all names in the welcome.
            var names = SeatNames.Of(_party);
            SeatNames.Set(names);
            Server.Broadcast(new Roster(names));

            if (admit.NewToken != null)
            {
                Log.Info(admit.Created
                    ? $"{ModEntry.LogTag} new seat \"{admit.Seat.SeatId}\" = player {admit.Seat.PlayerId} from {from}"
                    : $"{ModEntry.LogTag} open seat \"{admit.Seat.SeatId}\" (player {admit.Seat.PlayerId}) claimed with a new token from {from}");
            }
            else
            {
                Log.Info($"{ModEntry.LogTag} seat \"{admit.Seat.SeatId}\" (player {admit.Seat.PlayerId}) claimed from {from}");
            }
            var unlocks = PartyUnlocks.Current;
            bool mirror = unlocks?.Mode == PartyUnlocks.MirrorHost;
            return new Welcome(admit.Seat.PlayerId, admit.Seat.SeatId, _party.Name, admit.NewToken,
                unlocks?.Mode ?? PartyUnlocks.Everything, 2, names,
                mirror ? PartyUnlocks.ToJson(unlocks!.Unlocks) : null,
                mirror ? unlocks!.MaxStandardAscension : 0, mirror ? unlocks!.MaxBlitzAscension : 0);
        }
    }

    void OnSeatLost(ControlSeat seat, string reason)
    {
        if (Current != this) return;
        // The game's ENet host never notices a crashed client on its own; tell it.
        if (NetService.IsConnected && NetService.NetHost?.ConnectedPeerIds.Contains(seat.PlayerId) == true)
            NetService.DisconnectClient(seat.PlayerId, reason == "left" ? NetError.Quit : NetError.Timeout);

        if (reason is "timeout" or "error" && RunManager.Instance?.IsInProgress == true)
        {
            Popups.Message("Player disconnected",
                $"\"{seat.SeatId}\" lost connection ({reason}).\n\n" +
                "To continue together: open the pause menu, choose Save and Quit, then SpireParty Host and reopen this party. " +
                $"\"{seat.SeatId}\" then joins again with the same seat ID and token. The current room starts over.");
        }
    }

    void CheckStillRunning()
    {
        // Wait for the run to be cleaned up too: RunManager.CleanUp still writes (the final combat
        // replay) after the disconnect, and that write must stay inside the session.
        if (Current == this && !NetService.IsConnected && !RunManager.Instance.IsInProgress) Stop();
    }

    public void Stop()
    {
        if (Current != this) return;
        MainThread.EveryFrame -= CheckStillRunning;
        Current = null;
        Server.Dispose();
        SpirePartySession.End();
        SeatNames.Clear();
        PartyUnlocks.Set(null);
        GameAdapters.ProfileGuardAdapter.RestoreProgressFromDisk();
        Log.Info($"{ModEntry.LogTag} host session for \"{_party.Name}\" ended");
    }
}
