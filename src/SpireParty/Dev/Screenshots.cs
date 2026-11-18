using System;
using System.IO;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using SpireParty.UI;

namespace SpireParty.Dev;

/// <summary>
/// Saves a PNG of the game's own viewport (not the OS window) each time the visible screen changes:
/// room type, top overlay, map open, combat. Waits a moment after a change so animations settle.
/// File names: &lt;seq&gt;-&lt;screen&gt;.png in the given folder.
/// </summary>
public static class Screenshots
{
    const double SettleSeconds = 2.0;
    static string _dir = "";
    static bool _started;
    static string _lastKey = "", _pendingKey = "";
    static double _pendingSince;
    static int _seq;

    public static void Start(string dir)
    {
        if (_started) return;
        _started = true;
        _dir = dir;
        try { Directory.CreateDirectory(dir); }
        catch (Exception e) { Log.Warn($"{ModEntry.LogTag} screenshots: cannot create {dir}: {e.Message}"); return; }
        MainThread.EveryFrame += Tick;
        Log.Info($"{ModEntry.LogTag} screenshots to {dir}");
    }

    static string ScreenKey()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay != null) return overlay.GetType().Name;
        if (NMapScreen.Instance is { IsOpen: true }) return "map";
        var state = RunManager.Instance?.DebugOnlyGetState();
        if (state?.CurrentRoom == null) return "menu";
        string room = state.CurrentRoom.RoomType.ToString().ToLowerInvariant();
        if (CombatManager.Instance is { IsInProgress: true }) room += "-combat";
        return $"a{state.CurrentActIndex + 1}f{state.ActFloor}-{room}";
    }

    static void Tick()
    {
        string key;
        try { key = ScreenKey(); } catch { return; }
        double now = Time.GetTicksMsec() / 1000.0;
        if (key != _pendingKey) { _pendingKey = key; _pendingSince = now; return; }
        if (key == _lastKey || now - _pendingSince < SettleSeconds) return;
        _lastKey = key;
        Capture(key);
    }

    public static void Capture(string label)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            var image = tree.Root.GetTexture().GetImage();
            string safe = new string(label.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
            string path = Path.Combine(_dir, $"{++_seq:D4}-{safe}.png");
            image.SavePng(path);
        }
        catch (Exception e)
        {
            Log.Warn($"{ModEntry.LogTag} screenshot failed: {e.Message}");
        }
    }
}
