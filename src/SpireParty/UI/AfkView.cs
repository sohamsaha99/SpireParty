using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using SpireParty.Core;
using SpireParty.Net;

namespace SpireParty.UI;

/// <summary>
/// AFK auto-pass for the local player (DESIGN → Turn timer and AFK protection, step 2; M3 task 3.8).
/// Counts the turns the <see cref="TurnTimerView"/> ended (<see cref="AfkState"/>); after the party's
/// <c>afkStrikes</c> in a row the player is marked AFK and <see cref="TurnTimerView"/> ends their turns as
/// soon as they can act. Real input in the focused game window (mouse movement, a key or button) clears
/// it. The state goes to the host, which tells everyone; every player sees who is AFK, and the AFK player
/// sees how to come back.
/// </summary>
public static class AfkView
{
    const float MouseThreshold = 4f;

    static readonly AfkState State = new();
    static Vector2? _lastMouse;
    static CanvasLayer? _layer;
    static Label? _self, _others;
    static bool _started;

    public static bool IsAfk => State.IsAfk;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        TurnTimerView.TimedOut += _ =>
        {
            if (State.OnTimedOut(PartyGameplay.AfkStrikes)) Changed(true);
            else Log.Info($"{ModEntry.LogTag} AFK: {State.Strikes} timed-out turn(s) in a row");
        };
        TurnTimerView.EndedByPlayer += _ => State.OnEndedByPlayer();
        MainThread.EveryFrame += Tick;
    }

    static void Tick()
    {
        try { Update(); }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} AFK view stopped: {e}");
            MainThread.EveryFrame -= Tick;
        }
    }

    static void Update()
    {
        if (!SpirePartySession.IsActive)
        {
            State.Reset();
            _lastMouse = null;
            if (_layer != null && GodotObject.IsInstanceValid(_layer)) _layer.Visible = false;
            return;
        }
        if (State.IsAfk && RealInput() && State.OnInput()) Changed(false);
        UpdateBanners();
    }

    /// <summary>
    /// Mouse moved inside the window, or anything pressed, in the focused window only (test PCs run several
    /// games; a mouse passing over a window it does not focus must not count).
    /// </summary>
    static bool RealInput()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || !tree.Root.HasFocus()) { _lastMouse = null; return false; }
        var mouse = tree.Root.GetMousePosition();
        if (!tree.Root.GetVisibleRect().HasPoint(mouse)) { _lastMouse = null; return Input.IsAnythingPressed(); }
        bool moved = _lastMouse is Vector2 last && last.DistanceTo(mouse) > MouseThreshold;
        _lastMouse = mouse;
        return moved || Input.IsAnythingPressed();
    }

    static void Changed(bool afk)
    {
        Log.Info($"{ModEntry.LogTag} AFK: local player {(afk ? $"marked AFK after {State.Strikes} timed-out turns" : "is back")}");
        if (HostSession.Current != null) HostSession.Current.SetLocalAfk(afk);
        else ClientSession.ReportAfk(afk);
        _lastMouse = null;   // the next mouse move counts from now
    }

    static void UpdateBanners()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        if (_layer == null || !GodotObject.IsInstanceValid(_layer))
        {
            _layer = new CanvasLayer { Name = "SpirePartyAfk", Layer = 90 };
            var box = new VBoxContainer
            {
                AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -500, OffsetRight = 500, OffsetTop = 90,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _self = MakeLabel(new Color(1f, 0.85f, 0.3f), 28);
            _others = MakeLabel(Colors.White, 22);
            box.AddChild(_self);
            box.AddChild(_others);
            _layer.AddChild(box);
            tree.Root.AddChild(_layer);
        }
        _layer.Visible = true;
        _self!.Visible = State.IsAfk;
        _self.Text = "You are marked AFK – move the mouse or press a key to come back";

        ulong me = SpirePartySession.Current?.PlayerId ?? 0;
        var names = AfkRoster.PlayerIds.Where(id => id != me)
            .Select(id => SeatNames.TryGet(id, out var n) ? n : id.ToString()).ToList();
        _others!.Visible = names.Count > 0;
        _others.Text = "AFK: " + string.Join(", ", names);
    }

    static Label MakeLabel(Color color, int size)
    {
        var label = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeConstantOverride("outline_size", 8);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        return label;
    }
}
