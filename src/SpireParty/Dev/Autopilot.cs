using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.AutoSlay.Handlers;
using MegaCrit.Sts2.Core.AutoSlay.Handlers.Rooms;
using MegaCrit.Sts2.Core.AutoSlay.Handlers.Screens;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireParty.Core;

namespace SpireParty.Dev;

/// <summary>
/// Test autopilot (dev switch only): plays the local player's own seat in a SpireParty session.
/// Everything it does is what this player could do with the mouse, so nothing is forged (hard rule 6):
/// cards go through <c>CardModel.TryManualPlay</c> and End Turn through the same request as the
/// End Turn button; screens and rooms are handled by clicking their UI.
///
/// Reuses the game's own AutoSlay screen and room handlers where they only click UI (rewards, card
/// screens, shop). Not reused: AutoSlay's combat and event-combat code, which
/// gives its player buffs directly (local state change, a desync in co-op), and its test card
/// selector, which skips the choice sync (the other peers would wait forever). Rest site and treasure
/// are our own: AutoSlay picks Mend (co-op only, needs a target) and re-opens an opened chest on retry.
/// </summary>
public static class Autopilot
{
    const int TickMs = 300;
    static bool _started;
    static readonly Rng Random = new((ulong)System.Environment.TickCount64);

    static readonly Dictionary<Type, IScreenHandler> ScreenHandlers = new IScreenHandler[]
    {
        new RewardsScreenHandler(), new CardRewardScreenHandler(), new DeckUpgradeScreenHandler(),
        new DeckTransformScreenHandler(), new DeckCardSelectScreenHandler(),
        new SimpleCardSelectScreenHandler(), new ChooseACardScreenHandler(), new ChooseABundleScreenHandler(),
        new ChooseARelicScreenHandler(), new GameOverScreenHandler(), new CrystalSphereScreenHandler(),
        new ByrdEvolutionScreenHandler(),
    }.ToDictionary(h => h.ScreenType);

    static readonly Dictionary<RoomType, IRoomHandler> RoomHandlers = new()
    {
        [RoomType.Shop] = new ShopRoomHandler(DrainScreensUntil),
    };

    // Per-room memory so a vote or a room action is sent once.
    static string _roomKey = "";
    static bool _roomHandled;
    static int _roomTries;
    static bool _restChosen, _restProceeded, _chestOpened, _relicPicked, _treasureProceeded;
    // The map is still open with voting enabled for a moment after a room is entered. Vote only after it
    // has closed once in this room, and in an event only after our own event has finished.
    static bool _mapClosedSinceEntry, _eventDone, _eventSeen;
    static bool _votedOnMap;
    static int _lastTurnPlayed;
    static NEventOptionButton? _lastEventClick;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        Log.Info($"{ModEntry.LogTag} autopilot on (acts only in a SpireParty session)");
        _ = Loop();
    }

    static async Task Loop()
    {
        while (true)
        {
            await Task.Delay(TickMs);
            try
            {
                await Step();
            }
            catch (Exception e)
            {
                Log.Warn($"{ModEntry.LogTag} autopilot: {e.GetType().Name}: {e.Message}");
                await Task.Delay(2000);
            }
        }
    }

    static async Task Step()
    {
        if (!SpirePartySession.IsActive) return;
        var state = RunManager.Instance?.DebugOnlyGetState();
        if (state == null)
        {
            await Lobby();
            return;
        }
        if (state.CurrentRoom == null) return;
        var me = LocalContext.GetMe(state);
        if (me == null) return;

        string key = $"{state.CurrentActIndex}:{state.TotalFloor}:{state.CurrentRoom.RoomType}:{state.CurrentRoomCount}";
        if (key != _roomKey)
        {
            _roomKey = key;
            _roomHandled = false;
            _roomTries = 0;
            _restChosen = _restProceeded = _chestOpened = _relicPicked = _treasureProceeded = false;
            _mapClosedSinceEntry = _eventDone = _eventSeen = false;
            _votedOnMap = false;
            _lastEventClick = null;
        }

        // 1. Map vote, once per room. After Proceed the rewards screen stays under the open map.
        var overlay = NOverlayStack.Instance?.Peek();
        if (NMapScreen.Instance is not { IsOpen: true }) _mapClosedSinceEntry = true;
        // At the start of an act no room is entered yet (no map coordinate): the map is the first thing to do.
        bool actStart = !state.CurrentMapCoord.HasValue;
        bool canVote = actStart || (_mapClosedSinceEntry && (state.CurrentRoom.RoomType != RoomType.Event || _eventDone));
        if (canVote && NMapScreen.Instance is { IsOpen: true } map && (overlay == null || overlay is NRewardsScreen))
        {
            if (_votedOnMap || !map.IsTravelEnabled) return;
            var points = FindAll<NMapPoint>(map).Where(p => p.IsEnabled).ToList();
            if (points.Count == 0) return;
            var pick = Random.NextItem(points)!;
            Log.Info($"{ModEntry.LogTag} autopilot: map vote ({pick.Point.coord.row}, {pick.Point.coord.col})");
            _votedOnMap = true;
            pick.ForceClick();
            return;
        }

        // 2. A screen on top (rewards, card choices, game over), in and out of combat.
        if (overlay is Node node)
        {
            if (ScreenHandlers.TryGetValue(node.GetType(), out var handler))
            {
                await HandleScreen(node, handler);
                return;
            }
            if (node is NCardGridSelectionScreen grid)
            {
                await PickFromGrid(grid);
                return;
            }
        }

        // 3. Choosing cards from the hand (discard, exhaust, ...).
        if (NPlayerHand.Instance is { IsInCardSelection: true } hand)
        {
            await PickFromHand(hand);
            return;
        }

        // 4. Combat turn.
        if (CombatManager.Instance is { IsInProgress: true } combat)
        {
            await PlayTurn(me, combat);
            return;
        }
        _lastTurnPlayed = 0;

        // 5. Room actions.
        var type = state.CurrentRoom.RoomType;
        if (type == RoomType.Event)
        {
            ClickEvent();
            return;
        }
        if (type == RoomType.Treasure)
        {
            await Treasure();
            return;
        }
        if (type == RoomType.RestSite)
        {
            await RestSite();
            return;
        }
        // Shop and treasure: retry a few times (co-op waits can outlast the single-player handlers).
        if (!_roomHandled && _roomTries < 5 && RoomHandlers.TryGetValue(type, out var roomHandler))
        {
            _roomTries++;
            _roomHandled = await Run(roomHandler, $"room {type}");
        }
    }

    /// <summary>
    /// Rest site: Heal or Smith (Smith's upgrade screen is handled as an overlay), then Proceed.
    /// Never Mend: it heals a teammate and asks the player to point at one, which this autopilot does not do.
    /// </summary>
    static async Task RestSite()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var room = tree.Root.GetNodeOrNull<NRestSiteRoom>("/root/Game/RootSceneContainer/Run/RoomContainer/RestSiteRoom");
        if (room == null) return;
        if (!_restChosen)
        {
            var buttons = FindAll<NRestSiteButton>(room).Where(b => b.Option.IsEnabled).ToList();
            if (buttons.Count == 0) return;
            var preferred = buttons.Where(b => b.Option is HealRestSiteOption or SmithRestSiteOption).ToList();
            var others = buttons.Where(b => b.Option is not MendRestSiteOption).ToList();
            var pool = preferred.Count > 0 ? preferred : others;
            if (pool.Count == 0) { _restChosen = true; return; } // only Mend left: just proceed
            var pick = Random.NextItem(pool)!;
            Log.Info($"{ModEntry.LogTag} autopilot: rest site {pick.Option.GetType().Name}");
            _restChosen = true;
            pick.ForceClick();
            await Task.Delay(1000);
            return;
        }
        if (!_restProceeded && room.ProceedButton is { IsEnabled: true } proceed)
        {
            _restProceeded = true;
            Log.Info($"{ModEntry.LogTag} autopilot: rest site proceed");
            proceed.ForceClick();
        }
    }

    /// <summary>Treasure: open the chest once, pick one relic once, then Proceed (contested relics are settled by the game).</summary>
    static async Task Treasure()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var room = tree.Root.GetNodeOrNull<NTreasureRoom>("/root/Game/RootSceneContainer/Run/RoomContainer/TreasureRoom");
        if (room == null) return;
        if (!_chestOpened)
        {
            if (room.GetNodeOrNull<NClickableControl>("Chest") is not { } chest) return;
            _chestOpened = true;
            Log.Info($"{ModEntry.LogTag} autopilot: treasure chest");
            chest.ForceClick();
            await Task.Delay(1500);
            return;
        }
        if (!_relicPicked)
        {
            var relics = FindAll<NTreasureRoomRelicHolder>(room).Where(h => h.IsEnabled && h.IsVisibleInTree()).ToList();
            if (relics.Count > 0)
            {
                _relicPicked = true;
                Log.Info($"{ModEntry.LogTag} autopilot: treasure relic");
                Random.NextItem(relics)!.ForceClick();
                await Task.Delay(1000);
                return;
            }
        }
        if (!_treasureProceeded && room.ProceedButton is { IsEnabled: true } proceed)
        {
            _treasureProceeded = true;
            Log.Info($"{ModEntry.LogTag} autopilot: treasure proceed");
            proceed.ForceClick();
        }
    }

    static Node? _readiedScreen;

    /// <summary>New-run lobby: pick a random character, then Embark (= ready). Load lobby: Embark.</summary>
    static async Task Lobby()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        Control? screen = FindAll<NCharacterSelectScreen>(tree.Root).FirstOrDefault(s => s.IsVisibleInTree());
        screen ??= FindAll<NMultiplayerLoadGameScreen>(tree.Root).FirstOrDefault(s => s.IsVisibleInTree());
        if (screen == null || screen == _readiedScreen) return;

        // The host waits for the expected party, or the run would start with whoever is there.
        if (SpirePartySession.Current?.Role == SessionRole.Host)
        {
            int present = screen switch
            {
                NCharacterSelectScreen cs => cs.Lobby?.Players.Count ?? 0,
                _ => (HarmonyLib.AccessTools.Field(typeof(NMultiplayerLoadGameScreen), "_runLobby")?.GetValue(screen)
                        as MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.LoadRunLobby)?.PlayerCount ?? 0,
            };
            if (present < DevSwitches.ExpectPlayers) return;
        }

        if (screen is NCharacterSelectScreen)
        {
            var buttons = FindAll<NCharacterSelectButton>(screen).Where(b => !b.IsLocked && b.Visible).ToList();
            if (buttons.Count == 0) return;
            var pick = Random.NextItem(buttons)!;
            pick.Select();
            Log.Info($"{ModEntry.LogTag} autopilot: character {pick.Character.Id}");
            await Task.Delay(1000);
        }
        var confirm = screen.GetNodeOrNull<NConfirmButton>("ConfirmButton");
        if (confirm is not { IsEnabled: true }) return;
        _readiedScreen = screen;
        Log.Info($"{ModEntry.LogTag} autopilot: ready in {screen.GetType().Name}");
        confirm.ForceClick();
    }

    /// <summary>A shop purchase can open a screen (e.g. rewards) and wait on it; handle screens until it is done.</summary>
    static async Task DrainScreensUntil(Task pending, CancellationToken ct)
    {
        while (!pending.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            if (NOverlayStack.Instance?.Peek() is Node top && ScreenHandlers.TryGetValue(top.GetType(), out var h))
                await HandleScreen(top, h);
            await Task.Delay(100, ct);
        }
        await pending;
    }

    static Node? _lastScreen;
    static ulong _lastScreenAt;

    /// <summary>
    /// Runs a screen handler, but not twice in a row on the same screen within 30 s: at 15 fps a screen
    /// can take longer to close than the handler waits, and handling it again clicks its buttons a second
    /// time (seen with the enchant screen: a thrown Cancel, then a divergence). The rewards screen is the
    /// exception: it is handled again after each reward's own screen closes.
    /// </summary>
    static async Task HandleScreen(Node screen, IScreenHandler handler)
    {
        ulong now = Time.GetTicksMsec();
        if (screen is not NRewardsScreen && screen == _lastScreen && now - _lastScreenAt < 30_000) return;
        _lastScreen = screen;
        _lastScreenAt = now;
        await Run(handler, $"screen {screen.GetType().Name}");
    }

    static async Task<bool> Run(IHandler handler, string what)
    {
        using var cts = new CancellationTokenSource(handler.Timeout);
        try
        {
            await handler.HandleAsync(Random, cts.Token);
            return true;
        }
        catch (Exception e)
        {
            // Co-op waits (others still choosing) can outlast a handler written for single-player.
            Log.Info($"{ModEntry.LogTag} autopilot: {what} ended early: {e.GetType().Name}: {e.Message}");
            await Task.Delay(1000);
            return false;
        }
    }

    static async Task PlayTurn(Player me, CombatManager combat)
    {
        var pcs = me.PlayerCombatState;
        if (pcs == null || pcs.Phase != PlayerTurnPhase.Play || me.Creature.IsDead) return;
        int turn = pcs.TurnNumber;
        if (turn <= _lastTurnPlayed || combat.IsPlayerReadyToEndTurn(me)) return;
        _lastTurnPlayed = turn;

        // Test runs: the host ends long fights with the game's own networked console command,
        // which is enqueued as a synced action, so every peer runs it.
        if (DevSwitches.KillTurn is int killTurn && turn >= killTurn && SpirePartySession.Current?.Role == SessionRole.Host)
        {
            Log.Info($"{ModEntry.LogTag} autopilot: console 'kill all' on turn {turn}");
            Console("kill all");
            await Task.Delay(1500);
            if (!combat.IsInProgress) return;
        }

        var tried = new HashSet<CardModel>();
        for (int played = 0; played < 30; played++)
        {
            if (!StillMyTurn(me, combat, turn)) return;
            if (NOverlayStack.Instance?.Peek() != null || NPlayerHand.Instance is { IsInCardSelection: true })
            {
                _lastTurnPlayed = turn - 1; // let the screen be handled, then come back to this turn
                return;
            }
            var playable = PileType.Hand.GetPile(me).Cards
                .Where(c => !tried.Contains(c) && c.CanPlay(out _, out _)).ToList();
            if (playable.Count == 0) break;
            var card = Random.NextItem(playable)!;
            tried.Add(card);
            if (!card.TryManualPlay(Target(card, me))) continue;
            // Wait for the play to resolve before choosing the next card, as a person would; queuing
            // several plays at once makes the game cancel the ones that no longer have energy.
            var hand = PileType.Hand.GetPile(me);
            for (int w = 0; w < 30 && hand.Cards.Contains(card) && StillMyTurn(me, combat, turn); w++) await Task.Delay(100);
            await Task.Delay(300);
        }
        if (StillMyTurn(me, combat, turn) && !combat.IsPlayerReadyToEndTurn(me))
        {
            Log.Info($"{ModEntry.LogTag} autopilot: end turn {turn}");
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(me, turn));
        }
    }

    static void Console(string command)
    {
        var node = MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole.InstanceOrNull;
        var console = node == null ? null
            : HarmonyLib.AccessTools.Field(node.GetType(), "_devConsole")?.GetValue(node) as MegaCrit.Sts2.Core.DevConsole.DevConsole;
        if (console == null) { Log.Warn($"{ModEntry.LogTag} autopilot: dev console not available"); return; }
        var result = console.ProcessCommand(command);
        Log.Info($"{ModEntry.LogTag} autopilot: console -> {result.success}");
    }

    static bool StillMyTurn(Player me, CombatManager combat, int turn) =>
        combat.IsInProgress && me.PlayerCombatState is { Phase: PlayerTurnPhase.Play } p && p.TurnNumber == turn;

    static Creature? Target(CardModel card, Player me)
    {
        var combat = card.CombatState ?? me.Creature.CombatState;
        if (combat == null) return null;
        return card.TargetType switch
        {
            TargetType.AnyEnemy => Random.NextItem(combat.HittableEnemies.Where(e => e.IsAlive).ToList()),
            TargetType.AnyAlly => Random.NextItem(combat.PlayerCreatures.Where(c => c.IsAlive).ToList()),
            _ => null,
        };
    }

    /// <summary>Grid choice screens without an AutoSlay handler (e.g. NCombatPileCardSelectScreen).</summary>
    static Node? _gridScreen;
    static readonly HashSet<NGridCardHolder> GridPicked = new();

    /// <summary>
    /// Card-grid choice screens without a reused handler: NCombatPileCardSelectScreen and the enchant
    /// screen. Picks cards one at a time (remembered across retries, since pressing a picked card again
    /// un-picks it) and clicks the deepest enabled Confirm button: the enchant screen opens a preview
    /// with its own Confirm once enough cards are picked.
    /// </summary>
    static async Task PickFromGrid(NCardGridSelectionScreen screen)
    {
        if (screen != _gridScreen) { _gridScreen = screen; GridPicked.Clear(); }
        var grid = FindAll<NCardGrid>(screen).FirstOrDefault();
        for (int i = 0; i < 15 && GodotObject.IsInstanceValid(screen) && screen.IsVisibleInTree(); i++)
        {
            var confirm = FindAll<NConfirmButton>(screen).Where(b => b.IsEnabled && b.IsVisibleInTree())
                .OrderByDescending(Depth).FirstOrDefault();
            if (confirm != null)
            {
                Log.Info($"{ModEntry.LogTag} autopilot: {screen.GetType().Name} confirm ({GridPicked.Count} picked)");
                confirm.ForceClick();
                await Task.Delay(1500);
                continue;
            }
            var left = FindAll<NGridCardHolder>(screen).Where(h => !GridPicked.Contains(h)).ToList();
            if (grid == null || left.Count == 0) break;
            var holder = Random.NextItem(left)!;
            GridPicked.Add(holder);
            grid.EmitSignal(NCardGrid.SignalName.HolderPressed, holder);
            await Task.Delay(400);
        }
        await Task.Delay(500);
    }

    static int Depth(Node n) { int d = 0; for (var p = n.GetParent(); p != null; p = p.GetParent()) d++; return d; }

    static async Task PickFromHand(NPlayerHand hand)
    {
        var confirm = hand.GetNodeOrNull<NConfirmButton>("%SelectModeConfirmButton");
        for (int i = 0; i < 10 && hand.IsInCardSelection; i++)
        {
            if (confirm != null && confirm.IsEnabled) { confirm.ForceClick(); break; }
            var holders = FindAll<NHandCardHolder>(hand).Where(h => h.Visible).ToList();
            if (holders.Count == 0) break;
            var holder = Random.NextItem(holders)!;
            holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
            await Task.Delay(300);
        }
        await Task.Delay(500);
    }

    static void ClickEvent()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var room = tree.Root.GetNodeOrNull("/root/Game/RootSceneContainer/Run/RoomContainer/EventRoom");
        if (room == null) { if (_eventSeen) _eventDone = true; return; } // gone after we saw it: left for a fight
        _eventSeen = true;

        // Ancient events talk first; click through the dialogue.
        foreach (var ancient in FindAll<NAncientEventLayout>(room))
        {
            var hitbox = ancient.GetNodeOrNull<NButton>("%DialogueHitbox");
            if (hitbox is { Visible: true, IsEnabled: true })
            {
                hitbox.EmitSignal(NClickableControl.SignalName.Released, hitbox);
                return;
            }
        }

        var options = FindAll<NEventOptionButton>(room).Where(o => o.IsEnabled && !o.Option.IsLocked).ToList();
        if (options.Count == 0) { if (_lastEventClick != null) _eventDone = true; return; }
        // A shared choice waits for the others; don't click again while our choice is still on screen.
        if (_lastEventClick != null && GodotObject.IsInstanceValid(_lastEventClick) && options.Contains(_lastEventClick)) return;
        var safe = options.Where(o => o.Option.WillKillPlayer == null || o.Event.Owner == null || !o.Option.WillKillPlayer(o.Event.Owner)).ToList();
        // Finish the event with its Proceed option when it shows; only then may this seat vote on the map.
        var proceed = options.FirstOrDefault(o => o.Option.IsProceed);
        var pick = proceed ?? Random.NextItem(safe.Count > 0 ? safe : options)!;
        if (proceed != null) _eventDone = true;
        Log.Info($"{ModEntry.LogTag} autopilot: event option {pick.Event.Id.Entry}{(proceed != null ? " (proceed)" : "")}");
        _lastEventClick = pick;
        pick.ForceClick();
    }

    static List<T> FindAll<T>(Node start) where T : Node
    {
        var found = new List<T>();
        void Walk(Node n)
        {
            if (!GodotObject.IsInstanceValid(n)) return;
            if (n is T t) found.Add(t);
            foreach (var child in n.GetChildren()) Walk(child);
        }
        Walk(start);
        return found;
    }
}
