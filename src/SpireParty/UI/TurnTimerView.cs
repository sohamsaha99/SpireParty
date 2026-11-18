using System;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using SpireParty.Core;

namespace SpireParty.UI;

/// <summary>
/// The turn timer in each player's own client (DESIGN → Turn timer and AFK protection; M3 task 3.7).
/// Checked every frame, no game patch: when the local player can act on a new turn (phase Play, turn not
/// ended, alive), a <see cref="TurnTimer"/> of the party's length starts. A countdown shows above the End
/// Turn button, red for the last 10 s. At zero the client presses its own End Turn button
/// (<c>NEndTurnButton.CallReleaseLogic</c>, what a click does), so nothing is forged (hard rule 6).
/// While a mandatory choice is open (a card selection screen, or picking cards from the hand) it waits
/// and ends the turn right after (M3 decision). The host can pause it for everyone.
/// </summary>
public static class TurnTimerView
{
    /// <summary>The local player's turn ran out and the timer ended it (turn number). For the AFK count (3.8).</summary>
    public static event Action<int>? TimedOut;

    /// <summary>The local player ended a timed turn themselves (turn number). For the AFK count (3.8).</summary>
    public static event Action<int>? EndedByPlayer;

    static readonly TurnTimer Timer = new();
    static object? _combat;
    static int _turn;
    static bool _endRequested;
    static double _endRequestedAt;
    static Label? _label;
    static bool _started;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        MainThread.EveryFrame += Tick;
    }

    static double Now => Time.GetTicksMsec() / 1000.0;

    static void Tick()
    {
        try { Update(); }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} turn timer stopped: {e}");
            MainThread.EveryFrame -= Tick;
            Hide();
        }
    }

    static void Update()
    {
        int seconds = PartyGameplay.TurnTimerSeconds;
        var combat = CombatManager.Instance;
        if (!SpirePartySession.IsActive || seconds <= 0 || combat is not { IsInProgress: true })
        {
            Reset();
            return;
        }
        var me = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
        var pcs = me?.PlayerCombatState;
        bool myTurn = me != null && pcs != null && !me.Creature.IsDead && pcs.Phase == PlayerTurnPhase.Play
                      && !combat.IsPlayerReadyToEndTurn(me);
        if (!myTurn)
        {
            if (Timer.IsRunning)
            {
                if (_endRequested) Log.Info($"{ModEntry.LogTag} turn timer: turn {_turn} ended by the timer");
                else if (me != null && combat.IsPlayerReadyToEndTurn(me)) EndedByPlayer?.Invoke(_turn);   // not when the fight just ended
                if (_endRequested) TimedOut?.Invoke(_turn);
            }
            Timer.Stop();
            _endRequested = false;
            Hide();
            return;
        }

        // A new turn (turn numbers restart each combat, so the combat counts too).
        if (!ReferenceEquals(_combat, combat.DebugOnlyGetState()) || _turn != pcs!.TurnNumber || !Timer.IsRunning)
        {
            _combat = combat.DebugOnlyGetState();
            _turn = pcs!.TurnNumber;
            _endRequested = false;
            Timer.Start(Now, seconds, PartyGameplay.TimerPaused);
        }

        if (PartyGameplay.TimerPaused && !Timer.IsPaused) Timer.Pause(Now);
        if (!PartyGameplay.TimerPaused && Timer.IsPaused) Timer.Resume(Now);

        // AFK (and not paused by the host): no countdown, end the turn as soon as possible.
        bool autoPass = AfkView.IsAfk && !Timer.IsPaused;
        if (!Timer.IsExpired(Now) && !autoPass)
        {
            Show(Timer.IsPaused ? $"{TurnTimer.Format(Timer.Remaining(Now))} paused" : TurnTimer.Format(Timer.Remaining(Now)),
                 Timer.IsWarning(Now) && !Timer.IsPaused);
            return;
        }

        if (ChoiceOpen())
        {
            Show("0:00 – choose to end the turn", true);
            return;
        }
        // Press End Turn; if the press did not take (a card was still being played), press again shortly.
        if (_endRequested && Now - _endRequestedAt < 1.5) return;
        var button = NCombatRoom.Instance?.Ui?.EndTurnButton;
        if (button == null) return;
        if (!_endRequested) Log.Info($"{ModEntry.LogTag} turn timer: {(autoPass ? "AFK, passing" : "time is up on")} turn {_turn}, ending it");
        _endRequested = true;
        _endRequestedAt = Now;
        Show(autoPass ? "AFK" : "0:00", true);
        button.CallReleaseLogic();
    }

    /// <summary>A mandatory choice: a card selection screen on top, or picking cards from the hand.</summary>
    static bool ChoiceOpen() =>
        NOverlayStack.Instance?.Peek() is ICardSelectionScreen
        || NPlayerHand.Instance is { CurrentMode: NPlayerHand.Mode.SimpleSelect or NPlayerHand.Mode.UpgradeSelect };

    static void Reset()
    {
        Timer.Stop();
        _endRequested = false;
        _combat = null;
        Hide();
    }

    static void Show(string text, bool warning)
    {
        var button = NCombatRoom.Instance?.Ui?.EndTurnButton;
        if (button == null) return;
        if (_label == null || !GodotObject.IsInstanceValid(_label) || _label.GetParent() != button)
        {
            _label = new Label
            {
                Name = "SpirePartyTurnTimer",
                HorizontalAlignment = HorizontalAlignment.Center,
                Position = new Vector2(-40, -48),
                CustomMinimumSize = new Vector2(button.Size.X + 80, 40),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _label.AddThemeFontSizeOverride("font_size", 30);
            _label.AddThemeConstantOverride("outline_size", 8);
            _label.AddThemeColorOverride("font_outline_color", Colors.Black);
            button.AddChild(_label);
        }
        _label.Text = text;
        _label.AddThemeColorOverride("font_color", warning ? new Color(1f, 0.3f, 0.3f) : Colors.White);
        _label.Visible = true;
    }

    static void Hide()
    {
        if (_label != null && GodotObject.IsInstanceValid(_label)) _label.Visible = false;
    }
}
