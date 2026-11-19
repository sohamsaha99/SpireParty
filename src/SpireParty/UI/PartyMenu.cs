using System;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using SpireParty.Core;
using SpireParty.GameAdapters;
using SpireParty.Net;
using SpireParty.Platform;

namespace SpireParty.UI;

/// <summary>The forms behind the SpireParty Host and Join buttons.</summary>
public static class PartyMenu
{
    static NSubmenuStack? StackOf(NSubmenu submenu) => AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(submenu) as NSubmenuStack;

    static bool Available()
    {
        MainThread.Ensure();
        if (ModEntry.Adapters is { } report && !report.IsFeatureEnabled(Features_.DirectConnect))
        {
            Popups.Message("SpireParty unavailable", report.DisabledMessage() ?? "SpireParty is disabled for this game build.");
            return false;
        }
        if (SpirePartySession.IsActive)
        {
            Popups.Message("SpireParty", "A SpireParty session is already running.");
            return false;
        }
        return true;
    }

    public static void OpenHost(NSubmenu submenu)
    {
        if (!Available() || StackOf(submenu) is not { } stack) return;
        var store = new PartyStore(PlatformInfo.SpirePartyDataDir);

        var form = new Form("SpireParty Host");

        var parties = store.List();
        if (parties.Count > 0)
        {
            form.AddText("Open a party:", 18);
            form.AddChoiceList(parties.Select(p => ($"{p}{(PartyRunSave.Exists(store.RunSavePath(p)) ? "   (run in progress)" : "")}",
                (Action)(() => { form.Close(); RosterScreens.OpenParty(p, stack); }))));
        }
        var name = form.AddField("Or create a new party:", placeholder: "e.g. thursday-crew");
        form.AddButtons(
            ("Create", () =>
            {
                string folder = SeatRules.PartyFolderName(name.Text);
                if (folder.Length == 0) form.SetStatus("The party name needs at least one letter or digit.");
                else if (store.List().Contains(folder)) form.SetStatus($"A party called \"{folder}\" already exists.");
                else
                {
                    form.Close();
                    PickMode(store, name.Text, stack);
                }
            }),
            ("Cancel", form.Close));
        form.Show();
    }

    /// <summary>One party = one run = one mode (D4): the mode is chosen once, when the party is created.</summary>
    static void PickMode(PartyStore store, string partyName, NSubmenuStack stack)
    {
        var form = new Form($"Game mode for \"{partyName.Trim()}\"");
        form.AddText("The party plays one run in this mode. It cannot be changed later.", 18);
        bool modes = ModEntry.Adapters?.IsFeatureEnabled(Features_.GameModes) ?? true;
        var choices = PartyModes.All.Where(m => modes || m == PartyModes.Standard);
        form.AddChoiceList(choices.Select(m => ($"{PartyModes.Label(m)} — {PartyModes.Description(m)}", (Action)(() =>
        {
            try
            {
                var (folder, _) = store.Create(partyName, m);
                Log.Info($"{ModEntry.LogTag} created party \"{folder}\" ({m} mode)");
                string? error = HostSession.Start(folder, stack);
                if (error == null) form.Close();
                else form.SetStatus(error);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                form.SetStatus(e.Message);
            }
        }))));
        form.AddButtons(("Cancel", form.Close));
        form.Show();
    }

    public static void OpenJoin(NSubmenu submenu)
    {
        if (!Available() || StackOf(submenu) is not { } stack) return;
        var config = MachineConfig.Load(PlatformInfo.SpirePartyDataDir, out _);
        var last = config.RecentHosts.FirstOrDefault();

        var form = new Form("SpireParty Join");
        Godot.LineEdit? address = null, seat = null, name = null;
        if (config.RecentHosts.Count > 0)
        {
            form.AddText("Recent:", 18);
            foreach (var h in config.RecentHosts)
            {
                form.AddRow($"{h.SeatId} @ {h.Address}:{h.Port}",
                    ("Use", () =>
                    {
                        address!.Text = h.Port == config.GamePort ? h.Address : $"{h.Address}:{h.Port}";
                        seat!.Text = h.SeatId;
                        name!.Text = h.Name;
                    }),
                    // Forget only removes the entry from this list (config.json); the party and the seat are the host's.
                    ("Forget", () =>
                    {
                        var fresh = MachineConfig.Load(PlatformInfo.SpirePartyDataDir, out _);
                        if (fresh.Forget(h)) fresh.Save(PlatformInfo.SpirePartyDataDir);
                        Log.Info($"{ModEntry.LogTag} forgot recent host {h.SeatId} @ {h.Address}:{h.Port}");
                        form.Close();
                        OpenJoin(submenu);
                    }));
            }
        }
        address = form.AddField("Host address (Tailscale or Meshnet IP or name):",
            last == null ? "" : last.Port == config.GamePort ? last.Address : $"{last.Address}:{last.Port}", "100.x.y.z");
        seat = form.AddField("Seat ID:", last?.SeatId ?? "", "e.g. ravi");
        name = form.AddField("Your name (optional, shown to the party):", last?.Name ?? "", "e.g. Ravi");
        var token = form.AddField("Token (leave empty the first time you join):", placeholder: "amber-otter-41");

        bool busy = false;
        form.AddButtons(
            ("Join", async () =>
            {
                if (busy) return;
                busy = true;
                form.SetStatus("Connecting...");
                try
                {
                    string? error = await ClientSession.StartAsync(address.Text, seat.Text, token.Text, stack, name.Text);
                    if (error == null) form.Close();
                    else form.SetStatus(error);
                }
                catch (Exception e)
                {
                    Log.Error($"{ModEntry.LogTag} join failed: {e}");
                    form.SetStatus("Something went wrong. See the game log.");
                }
                finally
                {
                    busy = false;
                }
            }),
            ("Cancel", form.Close));
        form.Show();
    }
}
