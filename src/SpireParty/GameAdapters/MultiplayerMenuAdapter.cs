using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using SpireParty.UI;

namespace SpireParty.GameAdapters;

/// <summary>
/// Adds "SpireParty Host" and "SpireParty Join" to the game's multiplayer submenu
/// (postfix on NMultiplayerSubmenu._Ready). The vanilla buttons are left alone (hard rule 8).
/// </summary>
public sealed class MultiplayerMenuAdapter : GameAdapter
{
    public override string Name => "MultiplayerMenu";
    // The buttons are how the feature is reached; with no other adapter failing they always show.
    public override IReadOnlyList<string> Features => Array.Empty<string>();

    public override string? SelfCheck() =>
        Require(typeof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu._Ready), Type.EmptyTypes)
        ?? (AccessTools.Field(typeof(NSubmenu), "_stack") == null ? "field NSubmenu._stack not found" : null);

    public override void Apply(Harmony harmony) =>
        harmony.Patch(AccessTools.Method(typeof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu._Ready), Type.EmptyTypes),
            postfix: new HarmonyMethod(typeof(MultiplayerMenuAdapter), nameof(ReadyPostfix)));

    static void ReadyPostfix(NMultiplayerSubmenu __instance)
    {
        try
        {
            var container = __instance.GetNodeOrNull<Control>("ButtonContainer");
            var template = container?.GetNodeOrNull<NSubmenuButton>("HostButton");
            if (container == null || template == null)
            {
                Log.Warn($"{ModEntry.LogTag} multiplayer menu layout changed; SpireParty buttons not added");
                return;
            }
            AddButton(container, template, "SpirePartyHost", "SpireParty Host",
                "Host a party over Tailscale or Meshnet. Friends join with a seat ID.", "host",
                () => PartyMenu.OpenHost(__instance));
            AddButton(container, template, "SpirePartyJoin", "SpireParty Join",
                "Join a friend's party by address and seat ID.", "join",
                () => PartyMenu.OpenJoin(__instance));
        }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} could not add SpireParty menu buttons: {e}");
        }
    }

    static void AddButton(Control container, NSubmenuButton template, string name, string title, string description,
        string icon, Action onPressed)
    {
        NSubmenuButton button;
        if (!string.IsNullOrEmpty(template.SceneFilePath) && ResourceLoader.Load<PackedScene>(template.SceneFilePath) is { } scene)
            button = scene.Instantiate<NSubmenuButton>();
        else
            button = (NSubmenuButton)template.Duplicate((int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts | Node.DuplicateFlags.UseInstantiation));

        button.Name = name;
        container.AddChild(button);
        button.SetIcon(icon);
        if (button.FindChild("Title", true, false) is Control titleNode) SetText(titleNode, title);
        if (button.FindChild("Description", true, false) is Control descriptionNode) SetText(descriptionNode, description);
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => onPressed()));
        button.Visible = true;
    }

    /// <summary>The game's labels are Label / RichTextLabel subclasses; set text without localization.</summary>
    static void SetText(Control node, string text)
    {
        switch (node)
        {
            case RichTextLabel rich: rich.Text = text; break;
            case Label label: label.Text = text; break;
            default: node.Set("text", text); break;
        }
    }
}
