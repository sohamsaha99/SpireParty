using System;
using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using SpireParty.Core;
using SpireParty.Net;
using SpireParty.UI;

namespace SpireParty.Dev;

/// <summary>
/// --spireparty-chattest: once this process is in a run, drives the chat through the game's real input
/// pipeline (Input.ParseInputEvent, so ChatInputAdapter and the LineEdit see ordinary key events):
/// T opens the chat, the text is typed key by key, Enter sends; once the line is back and showing, T hides
/// it. Then it sends a burst of lines (one long) to fill the history, opens the chat with T and closes it
/// with Esc. Logs each step as "dev chat: ..."; with --spireparty-shots also saves chat-*.png there.
/// Test-only; inactive without the switch.
/// </summary>
public static class ChatTest
{
    const double StartDelaySeconds = 6;
    const int BurstLines = 14;

    enum Step { WaitForRun, Open, Type, Send, WaitForPeek, HidePeek, CheckHidden, Burst, PeekMany, HideMany, OpenMany, Escape, Check, Done }

    static Step _step = Step.WaitForRun;
    static double _inRunSince = -1, _stepAt;
    static string _text = "";
    static int _burstSent;
    static bool _started;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        MainThread.EveryFrame += Tick;
        Log.Info($"{ModEntry.LogTag} dev chat: test armed");
    }

    static double Now => Time.GetTicksMsec() / 1000.0;

    static void Tick()
    {
        try { Advance(); }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} dev chat: test stopped: {e}");
            MainThread.EveryFrame -= Tick;
        }
    }

    static void Advance()
    {
        if (_step == Step.Done) return;
        bool inRun = SpirePartySession.IsActive && RunManager.Instance?.IsInProgress == true;
        if (_step == Step.WaitForRun)
        {
            if (!inRun) { _inRunSince = -1; return; }
            if (_inRunSince < 0) _inRunSince = Now;
            if (Now - _inRunSince < StartDelaySeconds) return;
            _text = $"chat test from {SpirePartySession.Current!.SeatId} [ok]";
            Next(Step.Open);
            return;
        }
        if (Now < _stepAt) return;

        switch (_step)
        {
            case Step.Open:
                Key(global::Godot.Key.T, 't');
                Next(Step.Type);
                break;
            case Step.Type:
                Log.Info($"{ModEntry.LogTag} dev chat: after T typing={ChatView.IsTyping}");
                foreach (char c in _text) Key(global::Godot.Key.None, c);
                Next(Step.Send);
                break;
            case Step.Send:
                Shot("chat-1-typing");
                Key(global::Godot.Key.Enter, '\0');
                Log.Info($"{ModEntry.LogTag} dev chat: sent {_text.Length} chars");
                Next(Step.WaitForPeek);
                break;
            case Step.WaitForPeek:
                if (ChatView.Mode != ChatPanelMode.Peek) return;   // waits for our line to come back
                Shot("chat-2-showing");
                Next(Step.HidePeek, 1.0);
                break;
            case Step.HidePeek:
                Key(global::Godot.Key.T, 't');
                Next(Step.CheckHidden);
                break;
            case Step.CheckHidden:
                Log.Info($"{ModEntry.LogTag} dev chat: T while showing -> {ChatView.Mode}");
                Shot("chat-3-hidden");
                Next(Step.Burst);
                break;
            case Step.Burst:
                _burstSent++;
                string line = _burstSent == 7
                    ? "A long message to see how lines wrap: " + string.Join(" ", System.Linq.Enumerable.Repeat("wrap", 40))
                    : $"burst line {_burstSent} of {BurstLines}";
                PartyChat.Send(line);
                // 1.1 s apart: the host allows a burst of 5, then one message a second per player.
                Next(_burstSent < BurstLines ? Step.Burst : Step.PeekMany, 1.1);
                break;
            case Step.PeekMany:
                Log.Info($"{ModEntry.LogTag} dev chat: after the burst mode={ChatView.Mode}, history {PartyChat.History.Count} line(s)");
                Shot("chat-4-showing-many");
                Next(Step.HideMany, 0.5);
                break;
            case Step.HideMany:
                Key(global::Godot.Key.T, 't');   // hides the showing history
                Next(Step.OpenMany);
                break;
            case Step.OpenMany:
                Key(global::Godot.Key.T, 't');   // opens the chat with the input box
                Next(Step.Escape, 1.0);
                break;
            case Step.Escape:
                Log.Info($"{ModEntry.LogTag} dev chat: T, T -> {ChatView.Mode}");
                Shot("chat-5-open-many");
                Key(global::Godot.Key.Escape, '\0');
                Next(Step.Check);
                break;
            case Step.Check:
                Log.Info($"{ModEntry.LogTag} dev chat: Esc -> {ChatView.Mode}");
                Shot("chat-6-after-esc");
                Log.Info($"{ModEntry.LogTag} dev chat: done");
                _step = Step.Done;
                MainThread.EveryFrame -= Tick;
                break;
        }
    }

    static void Next(Step step, double delay = 0.5)
    {
        _step = step;
        _stepAt = Now + delay;
    }

    /// <summary>One key press and release, as the OS would deliver it.</summary>
    static void Key(Key keycode, char unicode)
    {
        foreach (bool pressed in new[] { true, false })
        {
            var e = new InputEventKey { Keycode = keycode, PhysicalKeycode = keycode, Pressed = pressed, Unicode = pressed ? unicode : 0 };
            Input.ParseInputEvent(e);
        }
    }

    static void Shot(string name)
    {
        if (DevSwitches.ShotsDir is not { } dir) return;
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            var image = tree.Root.GetTexture().GetImage();
            image.SavePng(Path.Combine(dir, name + ".png"));
        }
        catch (Exception e) { Log.Warn($"{ModEntry.LogTag} dev chat: screenshot failed: {e.Message}"); }
    }
}
