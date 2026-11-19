using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Checksums;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using SpireParty.Core;
using SpireParty.Net;
using SpireParty.Platform;
using FileAccess = System.IO.FileAccess;

namespace SpireParty.GameAdapters;

/// <summary>
/// Divergence diagnostics (M3 task 3.12). When the game finds a state divergence during a SpireParty session
/// (<c>ChecksumTracker.CompareChecksums</c> on the host finds a mismatch, or any peer receives a
/// <c>StateDivergenceMessage</c>), a bundle is saved under <c>user://mods/SpireParty/diagnostics/&lt;time&gt;/</c>
/// (<see cref="DiagnosticsBundle"/>): the game log so far, versions, OS, party.json without token hashes, the party's
/// run save (host), and the checksum context with both state dumps. The game's divergence popup
/// (<c>NErrorPopup.Create(NetErrorInfo)</c>) then also says to report it to the SpireParty developer, with the folder.
/// Outside a session nothing changes. Postfixes and a prefix only; the game's own handling runs unchanged.
/// Test switch <c>--spireparty-test-diverge=N</c> (one client only): flips that client's N-th checksum of the session,
/// so the host sees a mismatch; patched only when the switch is given.
/// </summary>
public sealed class DivergenceAdapter : GameAdapter
{
    public override string Name => "Divergence";
    // Diagnostics only: if this cannot be applied the game's own divergence handling still runs.
    public override IReadOnlyList<string> Features => Array.Empty<string>();

    const string ReportLine = "This happened in a SpireParty run. Please report it to the SpireParty developer and attach:";
    static readonly TimeSpan SameIncident = TimeSpan.FromMinutes(2);
    static readonly TimeSpan PopupWindow = TimeSpan.FromMinutes(10);

    static System.Reflection.MethodInfo? Compare => AccessTools.Method(typeof(ChecksumTracker), "CompareChecksums");
    static System.Reflection.MethodInfo? Received => AccessTools.Method(typeof(ChecksumTracker), "OnReceivedStateDivergenceMessage",
        new[] { typeof(StateDivergenceMessage), typeof(ulong) });
    static System.Reflection.MethodInfo? PopupCreate => AccessTools.Method(typeof(NErrorPopup), nameof(NErrorPopup.Create), new[] { typeof(NetErrorInfo) });
    static System.Reflection.MethodInfo? Generate => AccessTools.Method(typeof(ChecksumTracker), nameof(ChecksumTracker.GenerateChecksum),
        new[] { typeof(NetFullCombatState) });

    public override string? SelfCheck()
    {
        if (Compare == null) return "method ChecksumTracker.CompareChecksums not found";
        if (Received == null) return "method ChecksumTracker.OnReceivedStateDivergenceMessage not found";
        if (PopupCreate == null) return "method NErrorPopup.Create(NetErrorInfo) not found";
        if (AccessTools.Field(typeof(ChecksumTracker), "_checksums") == null) return "field ChecksumTracker._checksums not found";
        if (AccessTools.Field(typeof(NErrorPopup), "_body") == null) return "field NErrorPopup._body not found";
        var tracked = AccessTools.Inner(typeof(ChecksumTracker), "TrackedChecksum");
        if (tracked == null || new[] { "data", "context", "fullState" }.Any(f => AccessTools.Field(tracked, f) == null))
            return "type ChecksumTracker.TrackedChecksum changed";
        return null;
    }

    public override void Apply(Harmony harmony)
    {
        harmony.Patch(Compare, postfix: new HarmonyMethod(typeof(DivergenceAdapter), nameof(ComparePostfix)));
        harmony.Patch(Received, prefix: new HarmonyMethod(typeof(DivergenceAdapter), nameof(ReceivedPrefix)));
        harmony.Patch(PopupCreate, postfix: new HarmonyMethod(typeof(DivergenceAdapter), nameof(PopupPostfix)));
        if (Dev.DevSwitches.TestDiverge is { } n && Generate != null)
        {
            harmony.Patch(Generate, postfix: new HarmonyMethod(typeof(DivergenceAdapter), nameof(TestDivergePostfix)));
            Log.Warn($"{ModEntry.LogTag} TEST: this process will flip its checksum #{n} of a SpireParty session (--spireparty-test-diverge)");
        }
    }

    // ---- hooks (main thread: net messages are handled in the game's frame)

    static void ComparePostfix(object localChecksum, NetChecksumData remoteChecksum, ulong remoteId)
    {
        try
        {
            if (!SpirePartySession.IsActive) return;
            var local = Read(localChecksum);
            if (local.Data.checksum == remoteChecksum.checksum) return;
            Record($"Host found a mismatch for player {remoteId}", local, remoteChecksum.checksum, remoteState: null);
        }
        catch (Exception e) { Log.Error($"{ModEntry.LogTag} divergence diagnostics failed: {e}"); }
    }

    static void ReceivedPrefix(ChecksumTracker __instance, StateDivergenceMessage message, ulong senderId)
    {
        try
        {
            if (!SpirePartySession.IsActive) return;
            var list = AccessTools.Field(typeof(ChecksumTracker), "_checksums").GetValue(__instance) as IList;
            var local = list?.Cast<object>().Select(Read).FirstOrDefault(t => t.Data.id == message.senderChecksum.id);
            string what = $"Divergence report received from player {senderId}";
            if (local == null) what += $" for checksum id {message.senderChecksum.id}, which is no longer in the local list";
            Record(what, local, message.senderChecksum.checksum, message.senderCombatState?.ToString());
        }
        catch (Exception e) { Log.Error($"{ModEntry.LogTag} divergence diagnostics failed: {e}"); }
    }

    static void PopupPostfix(NetErrorInfo info, NErrorPopup? __result)
    {
        try
        {
            if (__result == null || info.GetReason() != NetError.StateDivergence) return;
            bool recent = _lastBundle != null && DateTime.UtcNow - _lastBundleAt < PopupWindow;
            if (!SpirePartySession.IsActive && !recent) return;
            if (!recent) Record("The game reported a state divergence (no checksum details reached this game)", null, null, null);
            var field = AccessTools.Field(typeof(NErrorPopup), "_body");
            string body = field.GetValue(__result) as string ?? "";
            field.SetValue(__result, $"{body}\n\n{ReportLine}\n{_lastBundle ?? DiagnosticsDir}");
            Log.Info($"{ModEntry.LogTag} divergence popup: added the report line for {_lastBundle ?? DiagnosticsDir}");
        }
        catch (Exception e) { Log.Error($"{ModEntry.LogTag} divergence popup text failed: {e}"); }
    }

    static int _generated;

    static void TestDivergePostfix(ref uint __result)
    {
        if (!SpirePartySession.IsActive) { _generated = 0; return; }
        if (++_generated != Dev.DevSwitches.TestDiverge) return;
        __result ^= 1u;
        Log.Warn($"{ModEntry.LogTag} TEST: checksum #{_generated} flipped (--spireparty-test-diverge)");
    }

    // ---- bundle

    sealed record Tracked(NetChecksumData Data, string Context, string? FullState);

    static Tracked Read(object tracked)
    {
        var t = Traverse.Create(tracked);
        return new Tracked(t.Field("data").GetValue<NetChecksumData>(), t.Field("context").GetValue<string>() ?? "",
            t.Field("fullState").GetValue()?.ToString());
    }

    static string DiagnosticsDir => Path.Combine(PlatformInfo.SpirePartyDataDir, "diagnostics");
    static string? _lastBundle;
    static DateTime _lastBundleAt;

    static void Record(string what, Tracked? local, uint? remoteChecksum, string? remoteState)
    {
        var s = SpirePartySession.Current;
        var text = new StringBuilder()
            .AppendLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC: {what}")
            .AppendLine($"We are: {s?.Role.ToString() ?? "(session ended)"}, seat \"{s?.SeatId}\", player {s?.PlayerId}")
            .AppendLine(local == null ? "Checksum: unknown"
                : $"Checksum id {local.Data.id}, context \"{local.Context}\", local {local.Data.checksum}, remote {remoteChecksum?.ToString() ?? "unknown"}")
            .AppendLine();
        if (local?.FullState != null) text.AppendLine("LOCAL STATE DUMP").AppendLine(local.FullState).AppendLine();
        if (remoteState != null) text.AppendLine("REMOTE STATE DUMP").AppendLine(remoteState).AppendLine();
        Log.Error($"{ModEntry.LogTag} state divergence in a party run: {what}");

        // One incident often fires several hooks (host mismatch, then the client's report): one bundle for it.
        if (_lastBundle != null && DateTime.UtcNow - _lastBundleAt < SameIncident && Directory.Exists(_lastBundle))
        {
            File.AppendAllText(Path.Combine(_lastBundle, "divergence.txt"), "----\n" + text);
            return;
        }

        var texts = new Dictionary<string, string> { ["divergence.txt"] = text.ToString(), ["info.txt"] = Info(s) };
        var copies = new Dictionary<string, string>();
        if (GameLogPath() is { } log) copies["game.log"] = log;
        if (s?.PartyDir is { } partyDir)
        {
            string partyJson = Path.Combine(partyDir, PartyStore.PartyFileName);
            if (File.Exists(partyJson) && DiagnosticsBundle.WithoutTokenHashes(ReadShared(partyJson)) is { } clean)
                texts["party.json"] = clean;
            if (HostSession.Current?.RunSavePath is { } save && File.Exists(save)) copies[Path.GetFileName(save)] = save;
        }
        int keep = MachineConfig.Load(PlatformInfo.SpirePartyDataDir, out _).DiagnosticsKept;
        _lastBundle = DiagnosticsBundle.Write(DiagnosticsDir, DateTime.Now, texts, copies, keep);
        _lastBundleAt = DateTime.UtcNow;
        Log.Error($"{ModEntry.LogTag} divergence diagnostics saved to {_lastBundle}");
    }

    static string Info(SessionContext? s)
    {
        var release = ReleaseInfoManager.Instance.ReleaseInfo;
        return new StringBuilder()
            .AppendLine($"SpireParty {ModEntry.Version}")
            .AppendLine($"Game {release?.Version ?? "unknown"} ({release?.Commit ?? "unknown"}), branch {release?.Branch ?? "unknown"}")
            .AppendLine($"OS {PlatformInfo.OsName}")
            .AppendLine($"Role {s?.Role.ToString() ?? "(session ended)"}, party \"{s?.PartyName}\", seat \"{s?.SeatId}\", player {s?.PlayerId}")
            .AppendLine($"Mode {HostSession.Current?.Mode ?? "(see the host's bundle)"}")
            .AppendLine($"Party settings: enemy health {PartyGameplay.EnemyHealthMultiplier}, turn timer {PartyGameplay.TurnTimerSeconds} s, AFK after {PartyGameplay.AfkStrikes}")
            .AppendLine($"Command line: {string.Join(" ", OS.GetCmdlineArgs())}")
            .ToString();
    }

    static string ReadShared(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var r = new StreamReader(f);
        return r.ReadToEnd();
    }

    /// <summary>The log this process writes: --log-file if given, else Godot's file log setting.</summary>
    static string? GameLogPath()
    {
        // Godot consumes --log-file itself (it is not in OS.GetCmdlineArgs), so read the process's own command line.
        var args = System.Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--log-file");
        string? path = i >= 0 && i + 1 < args.Length ? args[i + 1]
            : ProjectSettings.GetSetting("debug/file_logging/log_path").AsString();
        if (string.IsNullOrEmpty(path)) return null;
        path = ProjectSettings.GlobalizePath(path);
        return File.Exists(path) ? path : null;
    }
}
