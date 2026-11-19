using System;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using SpireParty.Core;
using SpireParty.Net;
using SpireParty.Platform;

namespace SpireParty.UI;

/// <summary>
/// The party screen, roster editor and backups list (DESIGN → Join and leave → Host flow; M2 task 2.8).
/// Each action goes through <see cref="PartyEditor"/> (backup first, refuse with a reason), then the screen
/// reopens with the result. Only opened from the main menu, never during a session.
/// </summary>
public static class RosterScreens
{
    const int Wide = 1180;

    static PartyEditor Editor(string folder)
    {
        string dataDir = PlatformInfo.SpirePartyDataDir;
        var store = new PartyStore(dataDir);
        var config = MachineConfig.Load(dataDir, out _);
        return new PartyEditor(store, new PartyBackups(store), folder, config.BackupsKept);
    }

    static string CharacterName(string id) =>
        id.Length == 0 ? "" : NewPlayer.FindCharacter(id)?.Title.GetFormattedText() ?? id;

    // ---- delete a party

    /// <summary>
    /// Moves the party's folder to mods/SpireParty/deleted/ (fail closed: nothing is erased at once); only the newest
    /// <c>deletedPartiesKept</c> deleted parties are kept. The game's profiles are never touched.
    /// </summary>
    static void ConfirmDelete(string folder, string name, NSubmenuStack stack)
    {
        string dataDir = PlatformInfo.SpirePartyDataDir;
        int keep = MachineConfig.Load(dataDir, out _).DeletedPartiesKept;
        bool running = PartyRunSave.Exists(new PartyStore(dataDir).RunSavePath(folder));
        Form.Confirm($"Delete party \"{name}\"?",
            (running ? "Its run in progress, roster, backups and history go with it. " : "Its roster, backups and history go with it. ") +
            $"The party leaves the Host list and its folder moves to mods/SpireParty/deleted/. The {keep} most recently deleted " +
            "parties are kept there, so you can move one back into parties/ by hand; older ones are removed for good.",
            "Delete",
            () =>
            {
                try
                {
                    var store = new PartyStore(dataDir);
                    string moved = store.Delete(folder, DateTime.Now, keep);
                    foreach (var w in store.Warnings) Log.Warn($"{ModEntry.LogTag} {w}");
                    Log.Info($"{ModEntry.LogTag} deleted party \"{folder}\" -> {moved}");
                    Popups.Message("Party deleted", $"\"{name}\" was deleted. Its folder is kept in mods/SpireParty/deleted/ for now.");
                }
                catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    Log.Warn($"{ModEntry.LogTag} delete party \"{folder}\" failed: {e.Message}");
                    OpenParty(folder, stack, $"Could not delete the party: {e.Message}", error: true);
                }
            });
    }

    // ---- party screen

    public static void OpenParty(string folder, NSubmenuStack stack, string? status = null, bool error = false)
    {
        var editor = Editor(folder);
        var party = editor.LoadParty();
        var form = new Form($"Party \"{party.Name}\"", 760);

        string summary;
        try
        {
            var run = editor.LoadRun();
            summary = run == null
                ? $"{PartyModes.Label(party.Mode)} mode. No run yet. Continue opens the lobby, where players join and pick characters."
                : $"{PartyModes.Label(party.Mode)} run in progress: act {run.CurrentActIndex + 1}, floor {run.MapPointHistory.Sum(a => a.Count)}, " +
                  $"{run.Players.Count} players{(run.Modifiers.Count > 0 ? $", modifiers: {string.Join(", ", run.Modifiers.Select(m => m.Id?.Entry))}" : "")}. " +
                  "Continue opens the lobby; players rejoin with their seat ID and token.";
        }
        catch (System.IO.InvalidDataException e)
        {
            summary = e.Message;
        }
        form.AddText(summary);
        var seats = editor.Seats(out _).Where(s => s.Status is "host" or "active" or "not in run").Select(s => s.Name);
        form.AddText($"Seats: {string.Join(", ", seats)}", 18);
        form.AddText(PartySettings.Of(party).Summary(), 18);

        form.AddButtons(
            ("Continue", () =>
            {
                string? err = HostSession.Start(folder, stack);
                if (err == null) form.Close();
                else form.SetStatus(err);
            }),
            ("Roster", () => { form.Close(); OpenRoster(folder, stack); }),
            ("Settings", () => { form.Close(); OpenSettings(folder, stack); }),
            ("Backups", () => { form.Close(); OpenBackups(folder, stack); }),
            ("Delete", () => { form.Close(); ConfirmDelete(folder, party.Name, stack); }),
            ("Back", form.Close));
        if (status != null) form.SetStatus(status, error);
        form.Show();
    }


    // ---- settings

    /// <summary>Max players, enemy health, turn timer and AFK strikes (M3 task 3.6). Used from the next Continue.</summary>
    public static void OpenSettings(string folder, NSubmenuStack stack, string? status = null, bool error = false)
    {
        var editor = Editor(folder);
        var party = editor.LoadParty();
        var current = PartySettings.Of(party);
        var form = new Form($"Settings: {party.Name}", 760);
        form.AddText("Changes are backed up and take effect the next time you press Continue.", 18);

        static int IndexOf<T>(T[] items, T value) => Math.Max(0, Array.IndexOf(items, value));
        var maxPlayers = form.AddOptions("Max players", PartySettings.MaxPlayerChoices.Select(n => n.ToString()),
            IndexOf(PartySettings.MaxPlayerChoices, current.MaxPlayers));
        // A hand-edited value that is not in the list is offered too, so opening and saving never changes it.
        var health = PartySettings.EnemyHealthChoices.Contains(current.EnemyHealthMultiplier)
            ? PartySettings.EnemyHealthChoices
            : PartySettings.EnemyHealthChoices.Append(current.EnemyHealthMultiplier).OrderBy(x => x).ToArray();
        var healthBox = form.AddOptions("Enemy health (1 = the game's own co-op scaling)", health.Select(PartySettings.Health),
            IndexOf(health, current.EnemyHealthMultiplier));
        var timer = form.AddOptions("Turn timer", PartySettings.TurnTimerChoices.Select(PartySettings.Timer),
            IndexOf(PartySettings.TurnTimerChoices, current.TurnTimerSeconds));
        var afk = form.AddOptions("Mark a player AFK after this many timed-out turns in a row", PartySettings.AfkStrikeChoices.Select(n => n.ToString()),
            IndexOf(PartySettings.AfkStrikeChoices, current.AfkStrikes));

        form.AddButtons(
            ("Save", () =>
            {
                var chosen = new PartySettings(
                    PartySettings.MaxPlayerChoices[maxPlayers.Selected],
                    health[healthBox.Selected],
                    PartySettings.TurnTimerChoices[timer.Selected],
                    PartySettings.AfkStrikeChoices[afk.Selected]);
                string? err = editor.SaveSettings(chosen, out string changed);
                if (err != null) { form.SetStatus(err); return; }
                form.Close();
                OpenParty(folder, stack, changed.Length == 0 ? "Nothing changed." : $"Saved: {changed}.", false);
            }),
            ("Back", () => { form.Close(); OpenParty(folder, stack); }));
        if (status != null) form.SetStatus(status, error);
        form.Show();
    }
    // ---- roster

    public static void OpenRoster(string folder, NSubmenuStack stack, string? status = null, bool error = false)
    {
        var editor = Editor(folder);
        var party = editor.LoadParty();
        var form = new Form($"Roster: {party.Name}", Wide);

        void Do(Func<string?> action, string success)
        {
            form.Close();
            string? result;
            try { result = action(); }
            catch (Exception e)
            {
                Log.Error($"{ModEntry.LogTag} roster edit failed: {e}");
                result = $"Something went wrong ({e.Message}). Nothing was changed if a backup could be put back; see the game log.";
            }
            if (result == null) Log.Info($"{ModEntry.LogTag} roster \"{folder}\": {success}");
            OpenRoster(folder, stack, result ?? success, result != null);
        }

        var seats = editor.Seats(out string? problem);
        bool hasRun = editor.HasRun;
        form.AddText(hasRun
            ? "Edits apply to the saved run. Each one is backed up first; the Backups screen can undo it."
            : "No run yet: you can rename seats, reset tokens and remove seats. Bench, restore and add need a run.", 18);

        foreach (var s in seats.Where(s => s.Status != "removed"))
        {
            string stats = s.Hp is { } hp ? $" · HP {hp}/{s.MaxHp} · {s.Gold} gold" : "";
            string ch = CharacterName(s.Character);
            string text = $"{s.Name} ({s.SeatId}) · {s.Status}{(s.Open ? " · open" : "")}{(ch.Length > 0 ? " · " + ch : "")}{stats}";

            var seatId = s.SeatId;
            var rename = ("Rename", (Action)(() => { form.Close(); OpenRename(folder, stack, seatId, s.Name); }));
            var reset = ("Reset token", (Action)(() => Do(() => editor.ResetToken(seatId),
                $"Seat \"{seatId}\" is open: the next player to join as \"{seatId}\" gets a new token.")));
            var remove = ("Remove", (Action)(() => Form.Confirm("Remove seat",
                $"Remove \"{s.Name}\" ({seatId}) from the party{(hasRun ? " and the run" : "")} for good? " +
                "Their deck, relics, HP and gold are kept only in backups. The seat ID cannot be used again.",
                "Remove", () => Do(() => editor.Remove(seatId), $"Removed \"{seatId}\"."))));

            switch (s.Status)
            {
                case "host":
                    form.AddRow(text, rename);
                    break;
                case "active":
                    form.AddRow(text, ("Bench", () => Do(() => editor.Bench(seatId),
                        $"Benched \"{seatId}\". Their state is kept; Restore puts them back as they were.")), reset, rename, remove);
                    break;
                case "not in run":
                    form.AddRow(text, ("Add to run", () => { form.Close(); OpenAdd(folder, stack, seatId, s.Name); }), reset, rename, remove);
                    break;
                case "benched":
                    form.AddRow(text, ("Restore", () => Do(() => editor.Restore(seatId),
                        $"Restored \"{seatId}\" with the same deck, relics, HP and gold.")), reset, rename, remove);
                    break;
            }
        }

        var removed = seats.Where(s => s.Status == "removed").Select(s => s.SeatId).ToList();
        if (removed.Count > 0) form.AddText($"Removed: {string.Join(", ", removed)}", 16);
        form.AddText("\"Open\" seats have no token yet: the next player to join with that seat ID claims the seat and is shown a new token.", 16);

        form.AddButtons(
            ("Add seat", () => { form.Close(); OpenAdd(folder, stack, "", ""); }),
            ("Settings", () => { form.Close(); OpenSettings(folder, stack); }),
            ("Backups", () => { form.Close(); OpenBackups(folder, stack); }),
            ("Back", () => { form.Close(); OpenParty(folder, stack); }));

        if (problem != null) form.SetStatus(problem);
        else if (status != null) form.SetStatus(status, error);
        form.Show();
    }

    static void OpenRename(string folder, NSubmenuStack stack, string seatId, string current)
    {
        var form = new Form($"Rename \"{seatId}\"");
        var name = form.AddField("Name shown to the party:", current);
        form.AddButtons(
            ("Save", () =>
            {
                string? err = Editor(folder).Rename(seatId, name.Text);
                if (err != null) { form.SetStatus(err); return; }
                form.Close();
                OpenRoster(folder, stack, $"\"{seatId}\" is now called \"{SeatNames.Clean(name.Text)}\".");
            }),
            ("Cancel", () => { form.Close(); OpenRoster(folder, stack); }));
        form.Show();
    }

    static void OpenAdd(string folder, NSubmenuStack stack, string seatId, string name)
    {
        var editor = Editor(folder);
        var party = editor.LoadParty();
        var choices = NewPlayer.Choices(party);
        bool existing = seatId.Length > 0;

        var form = new Form(existing ? $"Add \"{seatId}\" to the run" : "Add a seat");
        form.AddText($"The new player starts with the character's starter deck and relic. Catch-up: gold to the party's {GoldRule(party.CatchUpGold)}, " +
                     $"max HP {(party.CatchUpMaxHp == "matchLowest" ? "to the party's lowest" : "as the starter's")}.", 18);
        var seat = existing ? null : form.AddField("Seat ID (letters, digits, dashes):", "", "e.g. charlie");
        var display = form.AddField("Name shown to the party (optional):", name);
        var character = form.AddOptions("Character:", choices.Select(c => c.Title.GetFormattedText()));

        form.AddButtons(
            ("Add", () =>
            {
                if (choices.Count == 0) { form.SetStatus("No character is unlocked for this party."); return; }
                string id = existing ? seatId : SeatRules.Normalize(seat!.Text);
                var model = choices[Math.Max(0, character.Selected)];
                string? err;
                try { err = editor.Add(id, display.Text, model.Id.ToString(), NewPlayer.ForEditor); }
                catch (Exception e)
                {
                    Log.Error($"{ModEntry.LogTag} add seat failed: {e}");
                    err = $"Something went wrong ({e.Message}). See the game log.";
                }
                if (err != null) { form.SetStatus(err); return; }

                var added = editor.Seats(out _).FirstOrDefault(v => v.SeatId == id);
                Log.Info($"{ModEntry.LogTag} roster \"{folder}\": added \"{id}\" ({model.Id}), gold {added?.Gold}, max HP {added?.MaxHp}");
                form.Close();
                OpenRoster(folder, stack,
                    $"Added \"{added?.Name ?? id}\" as {model.Title.GetFormattedText()}: {added?.Gold} gold, {added?.MaxHp} max HP. " +
                    $"The seat is open: they join with seat ID \"{id}\" and an empty token.");
            }),
            ("Cancel", () => { form.Close(); OpenRoster(folder, stack); }));
        form.Show();
    }

    static string GoldRule(string rule) => rule switch
    {
        "highest" => "highest",
        "average" => "average",
        "lowest" => "lowest",
        _ => "starter amount (no catch-up)",
    };

    // ---- backups

    public static void OpenBackups(string folder, NSubmenuStack stack, string? status = null, bool error = false)
    {
        var editor = Editor(folder);
        var form = new Form($"Backups: {editor.LoadParty().Name}", Wide);
        var backups = editor.Backups();
        if (backups.Count == 0) form.AddText("No backups yet. One is taken before every roster edit.");
        else form.AddText("A backup is taken before every roster edit. Restoring one first backs up the current state, so it can be undone.", 18);

        foreach (var b in backups)
        {
            string name = b.Name;
            form.AddRow($"{b.Time:yyyy-MM-dd HH:mm:ss} · before: {b.Reason} · {b.Roster}",
                ("Restore", () => Form.Confirm("Restore backup",
                    $"Put the party back as it was at {b.Time:yyyy-MM-dd HH:mm:ss} (before \"{b.Reason}\")? The current state is backed up first.",
                    "Restore", () =>
                    {
                        form.Close();
                        string? err = editor.RestoreBackup(name, out string? undo);
                        if (err == null) Log.Info($"{ModEntry.LogTag} party \"{folder}\": restored backup {name}");
                        OpenBackups(folder, stack, err ?? $"Restored {name}. To undo, restore the newest backup (\"before restoring {name}\").", err != null);
                    })));
        }

        form.AddButtons(
            ("Roster", () => { form.Close(); OpenRoster(folder, stack); }),
            ("Back", () => { form.Close(); OpenParty(folder, stack); }));
        if (status != null) form.SetStatus(status, error);
        form.Show();
    }
}
