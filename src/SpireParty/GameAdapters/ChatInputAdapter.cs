using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using SpireParty.Core;
using SpireParty.UI;

namespace SpireParty.GameAdapters;

/// <summary>
/// The chat key for <see cref="ChatView"/>. During a SpireParty session, T (no Ctrl, Alt or Meta) that reaches
/// the game unhandled opens the chat, or hides a message that is only showing, and the game never sees it.
/// T is not one of the game's hotkeys (v0.113.1); Tab stays the map key. Prefix on
/// <c>NInputManager._UnhandledKeyInput</c>, where the game turns keys into hotkey actions. While the chat box
/// has focus it takes every typed key itself, so hotkeys never fire while typing.
///
/// Optional second patch: with the game's keyboard-only mode on, <c>NControllerManager.CheckForDirectionalKeyInput</c>
/// switches modes on any arrow key, even one typed in a text box, and moves focus away. It is skipped while
/// the chat box has focus. If that method is missing, only this guard is lost.
/// </summary>
public sealed class ChatInputAdapter : GameAdapter
{
    public override string Name => "Chat";
    public override IReadOnlyList<string> Features => new[] { Features_.Chat };

    public override string? SelfCheck() =>
        Require(typeof(NInputManager), nameof(NInputManager), "_UnhandledKeyInput", new[] { typeof(InputEvent) });

    public override void Apply(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(NInputManager), "_UnhandledKeyInput", new[] { typeof(InputEvent) }),
            prefix: new HarmonyMethod(typeof(ChatInputAdapter), nameof(UnhandledKeyPrefix)));

        var directional = AccessTools.Method(typeof(NControllerManager), "CheckForDirectionalKeyInput", new[] { typeof(InputEvent) });
        if (directional != null)
            harmony.Patch(directional, prefix: new HarmonyMethod(typeof(ChatInputAdapter), nameof(DirectionalPrefix)));
    }

    static bool UnhandledKeyPrefix(InputEvent inputEvent)
    {
        try
        {
            if (!SpirePartySession.IsActive) return true;
            if (inputEvent is not InputEventKey key || key.Keycode != Key.T) return true;
            if (key.CtrlPressed || key.AltPressed || key.MetaPressed) return true;
            if (!key.Pressed || key.Echo) return false;   // the chat owns T in a session; ignore its release and repeats
            ChatView.OnChatKey();
            return false;
        }
        catch (Exception)
        {
            return true;   // never break the game's input
        }
    }

    static bool DirectionalPrefix() => !ChatView.IsTyping;
}
