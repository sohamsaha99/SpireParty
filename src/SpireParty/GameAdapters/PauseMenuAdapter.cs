using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using SpireParty.Core;
using SpireParty.UI;

namespace SpireParty.GameAdapters;

/// <summary>
/// "Edit roster" in the host's pause menu during a SpireParty session (DESIGN → Join and leave → Host flow,
/// step 1; M2 task 2.9). It does what Save and Quit does (<c>NPauseMenu.CloseToMenu</c>: the run is already
/// saved at room entry and after each combat), then, once the session has ended and the main menu is up,
/// opens the roster editor for that party. Postfix on <c>NPauseMenu._Ready</c>, v0.113.1; the button is a
/// copy of the Save and Quit button.
/// </summary>
public sealed class PauseMenuAdapter : GameAdapter
{
    public override string Name => "PauseMenu";
    // Convenience only: without it the host uses Save and Quit, then SpireParty Host → the party → Roster.
    public override IReadOnlyList<string> Features => Array.Empty<string>();

    const string ButtonName = "SpirePartyEditRoster";
    static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(60);

    public override string? SelfCheck() =>
        Require(typeof(NPauseMenu), nameof(NPauseMenu), "_Ready", Type.EmptyTypes)
        ?? Require(typeof(NPauseMenu), nameof(NPauseMenu), "CloseToMenu", Type.EmptyTypes)
        ?? (AccessTools.Field(typeof(NPauseMenu), "_saveAndQuitButton") == null ? "field NPauseMenu._saveAndQuitButton not found" : null)
        ?? (AccessTools.Field(typeof(NPauseMenu), "_buttonContainer") == null ? "field NPauseMenu._buttonContainer not found" : null)
        ?? (AccessTools.Field(typeof(NSubmenu), "_stack") == null ? "field NSubmenu._stack not found" : null)
        ?? Require(typeof(NMainMenu), nameof(NMainMenu), nameof(NMainMenu.OpenMultiplayerSubmenu), Type.EmptyTypes);

    public override void Apply(Harmony harmony) =>
        harmony.Patch(AccessTools.Method(typeof(NPauseMenu), "_Ready", Type.EmptyTypes),
            postfix: new HarmonyMethod(typeof(PauseMenuAdapter), nameof(ReadyPostfix)));

    static void ReadyPostfix(NPauseMenu __instance)
    {
        try
        {
            if (SpirePartySession.Current is not { Role: SessionRole.Host, PartyDir: { } partyDir }) return;
            var container = (Control)AccessTools.Field(typeof(NPauseMenu), "_buttonContainer").GetValue(__instance)!;
            var saveAndQuit = (NPauseMenuButton)AccessTools.Field(typeof(NPauseMenu), "_saveAndQuitButton").GetValue(__instance)!;
            if (container.HasNode(ButtonName)) return;

            // Without DuplicateFlags.Signals: the game connects its buttons in code, and those must not be copied.
            var button = (NPauseMenuButton)saveAndQuit.Duplicate((int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts | Node.DuplicateFlags.UseInstantiation));
            button.Name = ButtonName;
            container.AddChild(button);
            container.MoveChild(button, saveAndQuit.GetIndex());
            button.GetNode<MegaLabel>("Label").SetTextAutoSize("Edit roster");
            button.Visible = true;

            string folder = Path.GetFileName(partyDir);
            button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
            {
                button.Disable();
                Log.Info($"{ModEntry.LogTag} Edit roster: saving and quitting, then opening the roster for \"{folder}\"");
                OpenRosterWhenBackAtMenu(folder);
                var close = (Task)AccessTools.Method(typeof(NPauseMenu), "CloseToMenu").Invoke(__instance, null)!;
                TaskHelper.RunSafely(close);
            }));
        }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} could not add Edit roster to the pause menu: {e}");
        }
    }

    /// <summary>Waits until the session has ended and the main menu is showing, then opens the roster.</summary>
    static void OpenRosterWhenBackAtMenu(string folder)
    {
        var deadline = DateTime.UtcNow + OpenTimeout;
        void Poll()
        {
            if (DateTime.UtcNow > deadline)
            {
                MainThread.EveryFrame -= Poll;
                Log.Warn($"{ModEntry.LogTag} Edit roster: the main menu did not come back in time; open the roster from SpireParty Host");
                return;
            }
            if (SpirePartySession.IsActive || NGame.Instance?.MainMenu is not { } menu || !menu.IsInsideTree()) return;
            MainThread.EveryFrame -= Poll;
            try
            {
                var submenu = menu.OpenMultiplayerSubmenu();
                if (AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(submenu) is NSubmenuStack stack)
                    RosterScreens.OpenRoster(folder, stack, "Saved and quit. Edit the roster, then go Back and press Continue to reopen the party.", error: false);
            }
            catch (Exception e)
            {
                Log.Error($"{ModEntry.LogTag} Edit roster: could not open the roster: {e}");
            }
        }
        MainThread.EveryFrame += Poll;
    }
}
