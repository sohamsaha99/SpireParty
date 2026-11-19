using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using SpireParty.Core;
using SpireParty.Net;
using SpireParty.Platform;
using SpireParty.UI;

namespace SpireParty.GameAdapters;

/// <summary>
/// Developer and test switches, like the game's own --fastmp. Ignored unless passed on the command line:
///   --spireparty-host=&lt;party&gt;                     open (or create) a party and host it
///   --spireparty-mode=&lt;mode&gt;                      with --spireparty-host: the mode of a party it creates (standard, daily, custom, blitz, dread)
///   --spireparty-join=&lt;address&gt;,&lt;seat&gt;[,&lt;token&gt;[,&lt;name&gt;]]  join a party
///   --spireparty-roster=&lt;party&gt;,&lt;op&gt;,&lt;seat&gt;[,&lt;character&gt;][;...]  roster edits through the roster editor
///       (backed up, refused with a reason like in the menus): bench, restore, add (with a character), reset (token),
///       remove, backup (restore a backup by name, or "latest"); then go on with --spireparty-host if also given.
///       A refused edit stops here (no hosting), so a test sees it at once.
/// Postfix on NMainMenu.CheckCommandLineArgs; runs once per process.
/// </summary>
public sealed class DevLaunchAdapter : GameAdapter
{
    public override string Name => "DevLaunch";
    public override IReadOnlyList<string> Features => Array.Empty<string>();

    static bool _done;

    public override string? SelfCheck() =>
        Require(typeof(NMainMenu), nameof(NMainMenu), "CheckCommandLineArgs", Type.EmptyTypes)
        ?? Require(typeof(NMainMenu), nameof(NMainMenu), nameof(NMainMenu.OpenMultiplayerSubmenu), Type.EmptyTypes);

    public override void Apply(Harmony harmony) =>
        harmony.Patch(AccessTools.Method(typeof(NMainMenu), "CheckCommandLineArgs", Type.EmptyTypes),
            postfix: new HarmonyMethod(typeof(DevLaunchAdapter), nameof(Postfix)));

    static void Postfix(NMainMenu __instance)
    {
        if (_done) return;
        Dev.DevSwitches.StartAll();
        bool host = CommandLineHelper.TryGetValue("spireparty-host", out string? party) && !string.IsNullOrWhiteSpace(party);
        bool join = CommandLineHelper.TryGetValue("spireparty-join", out string? target) && !string.IsNullOrWhiteSpace(target);
        bool roster = CommandLineHelper.TryGetValue("spireparty-roster", out string? rosterArgs) && !string.IsNullOrWhiteSpace(rosterArgs);
        if (!host && !join && !roster) return;
        _done = true;
        MainThread.Ensure();

        if (roster)
        {
            string? error = RosterEdits(rosterArgs!);
            if (error != null)
            {
                Log.Error($"{ModEntry.LogTag} dev launch: roster edit refused: {error}");
                Popups.Message("SpireParty roster edit", error);
                return;
            }
            if (!host && !join) return;
        }

        if (ModEntry.Adapters is { } report && !report.IsFeatureEnabled(Features_.DirectConnect))
        {
            Log.Error($"{ModEntry.LogTag} dev launch ignored: {report.DisabledMessage()}");
            return;
        }

        var submenu = __instance.OpenMultiplayerSubmenu();
        if (AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(submenu) is not NSubmenuStack stack) return;

        if (host)
        {
            var store = new PartyStore(PlatformInfo.SpirePartyDataDir);
            string folder = SeatRules.PartyFolderName(party);
            if (!store.List().Contains(folder)) store.Create(party!, Dev.DevSwitches.Mode ?? PartyModes.Standard);
            string? error = HostSession.Start(folder, stack);
            if (error != null) Popups.Message("SpireParty Host", error);
            Log.Info($"{ModEntry.LogTag} dev launch: host \"{folder}\" -> {error ?? "ok"}");
            return;
        }

        string[] parts = target!.Split(',');
        string address = parts[0].Trim();
        string seat = parts.Length > 1 ? parts[1].Trim() : "";
        string token = parts.Length > 2 ? parts[2].Trim() : "";
        if (token.Length == 0) token = Dev.DevSwitches.ReadTestToken();
        string name = parts.Length > 3 ? parts[3].Trim() : "";
        _ = JoinAsync(address, seat, token, name, stack);
    }

    /// <summary>Runs every --spireparty-roster edit in order; returns the first refusal (later edits are not run).</summary>
    static string? RosterEdits(string args)
    {
        var ops = RosterSwitch.Parse(args, out string? parseError);
        if (parseError != null) return $"--spireparty-roster: {parseError}";
        string dataDir = PlatformInfo.SpirePartyDataDir;
        var store = new PartyStore(dataDir);
        var config = MachineConfig.Load(dataDir, out _);
        foreach (var op in ops)
        {
            string folder = SeatRules.PartyFolderName(op.Party);
            var editor = new PartyEditor(store, new PartyBackups(store), folder, config.BackupsKept);
            string? undo = null;
            string? error = op.Op switch
            {
                "bench" => editor.Bench(op.Arg),
                "restore" => editor.Restore(op.Arg),
                "add" => editor.Add(op.Arg, op.Arg, op.Character, NewPlayer.ForEditor),
                "reset" => editor.ResetToken(op.Arg),
                "remove" => editor.Remove(op.Arg),
                "backup" => editor.RestoreBackup(op.Arg == "latest" ? editor.Backups().FirstOrDefault()?.Name ?? "" : op.Arg, out undo),
                _ => "unknown op",
            };
            if (error != null) return $"{op.Op} {op.Arg} in \"{folder}\": {error}";
            var seat = editor.Seats(out _).FirstOrDefault(s => s.SeatId == SeatRules.Normalize(op.Arg));
            Log.Info($"{ModEntry.LogTag} dev launch: roster {op.Op} {op.Arg} in \"{folder}\" -> ok" +
                     (seat == null ? "" : $" (seat {seat.SeatId}: {seat.Status}, {seat.Character}, HP {seat.Hp}/{seat.MaxHp}, gold {seat.Gold}, open {seat.Open})") +
                     (undo == null ? "" : $" (undo backup {undo})"));
        }
        return null;
    }

    static async System.Threading.Tasks.Task JoinAsync(string address, string seat, string token, string name, NSubmenuStack stack)
    {
        try
        {
            string? error = await ClientSession.StartAsync(address, seat, token, stack, name);
            if (error != null) Popups.Message("SpireParty Join", error);
            Log.Info($"{ModEntry.LogTag} dev launch: join {address} as \"{seat}\" -> {error ?? "ok"}");
        }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} dev launch join failed: {e}");
        }
    }
}
