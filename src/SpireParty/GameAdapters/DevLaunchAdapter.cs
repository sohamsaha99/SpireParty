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
///   --spireparty-join=&lt;address&gt;,&lt;seat&gt;[,&lt;token&gt;[,&lt;name&gt;]]  join a party
///   --spireparty-addseat=&lt;party&gt;,&lt;seat&gt;,&lt;character&gt;  add a seat and a new player to the party's saved run
///       through the roster editor (backed up; the seat is open), then go on with --spireparty-host if also given
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
        bool host = CommandLineHelper.TryGetValue("spireparty-host", out string? party) && !string.IsNullOrWhiteSpace(party);
        bool join = CommandLineHelper.TryGetValue("spireparty-join", out string? target) && !string.IsNullOrWhiteSpace(target);
        bool addSeat = CommandLineHelper.TryGetValue("spireparty-addseat", out string? addArgs) && !string.IsNullOrWhiteSpace(addArgs);
        if (!host && !join && !addSeat) return;
        _done = true;
        MainThread.Ensure();

        if (addSeat)
        {
            try
            {
                Log.Info($"{ModEntry.LogTag} dev launch: addseat -> {AddSeat(addArgs!)}");
            }
            catch (Exception e)
            {
                Log.Error($"{ModEntry.LogTag} dev launch: addseat failed: {e}");
                Popups.Message("SpireParty add seat", e.Message);
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
            if (!store.List().Contains(folder)) store.Create(party!);
            string? error = HostSession.Start(folder, stack);
            if (error != null) Popups.Message("SpireParty Host", error);
            Log.Info($"{ModEntry.LogTag} dev launch: host \"{folder}\" -> {error ?? "ok"}");
            return;
        }

        string[] parts = target!.Split(',');
        string address = parts[0].Trim();
        string seat = parts.Length > 1 ? parts[1].Trim() : "";
        string token = parts.Length > 2 ? parts[2].Trim() : "";
        string name = parts.Length > 3 ? parts[3].Trim() : "";
        _ = JoinAsync(address, seat, token, name, stack);
    }

    /// <summary>&lt;party&gt;,&lt;seat&gt;,&lt;character&gt; through the roster editor; throws with the refusal.</summary>
    static string AddSeat(string args)
    {
        string[] a = args.Split(',').Select(x => x.Trim()).ToArray();
        if (a.Length != 3) throw new ArgumentException("--spireparty-addseat=<party>,<seat>,<character>");
        string dataDir = PlatformInfo.SpirePartyDataDir;
        var store = new PartyStore(dataDir);
        var config = MachineConfig.Load(dataDir, out _);
        var editor = new PartyEditor(store, new PartyBackups(store), SeatRules.PartyFolderName(a[0]), config.BackupsKept);
        string? error = editor.Add(a[1], a[1], a[2], NewPlayer.ForEditor);
        if (error != null) throw new InvalidOperationException(error);
        var seat = editor.Seats(out _).First(s => s.SeatId == SeatRules.Normalize(a[1]));
        return $"seat \"{seat.SeatId}\" ({seat.Character}): HP {seat.Hp}/{seat.MaxHp}, gold {seat.Gold}, open {seat.Open}";
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
