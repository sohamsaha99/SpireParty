using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Daily;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Save;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Badges;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.Metrics;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using SpireParty.Core;
using SpireParty.Platform;

namespace SpireParty.GameAdapters;

/// <summary>
/// During a SpireParty session (host and clients), nothing reaches the player's profile:
/// progress updates, the progress file, stats, metrics and daily scores are skipped, and run
/// history goes to the party folder. When the session ends, progress is reloaded from disk so
/// in-memory changes made during the party run can never be saved later. Hard rule 1.
/// </summary>
public sealed class ProfileGuardAdapter : GameAdapter
{
    public override string Name => "ProfileGuard";
    public override IReadOnlyList<string> Features => new[] { Features_.DirectConnect };

    static readonly (Type Type, string Method, Type[] Args, string Prefix)[] Targets =
    {
        (typeof(SaveManager), nameof(SaveManager.UpdateProgressWithRunData), new[] { typeof(SerializableRun), typeof(bool) }, nameof(UpdateWithRunDataPrefix)),
        (typeof(SaveManager), nameof(SaveManager.UpdateProgressAfterCombatWon), new[] { typeof(Player), typeof(CombatRoom) }, nameof(SkipPrefix)),
        // The single writer of progress.save: SaveManager.SaveProgressFile, tutorial (FTUE) flags,
        // SetFtuesEnabled and the rest all end here. Patching only SaveProgressFile leaked FTUE
        // writes during the M1 gate (2026-11-17).
        (typeof(ProgressSaveManager), nameof(ProgressSaveManager.SaveProgress), Type.EmptyTypes, nameof(SkipPrefix)),
        // Combat replays (replays/latest.mcr) go to the party folder instead of the profile.
        (typeof(RunManager), nameof(RunManager.WriteReplay), new[] { typeof(bool) }, nameof(WriteReplayPrefix)),
        (typeof(SaveManager), nameof(SaveManager.SaveRunHistory), new[] { typeof(RunHistory) }, nameof(SaveRunHistoryPrefix)),
        (typeof(MetricUtilities), nameof(MetricUtilities.UploadRunMetrics), new[] { typeof(SerializableRun), typeof(bool), typeof(ulong) }, nameof(SkipPrefix)),
        (typeof(MetricUtilities), nameof(MetricUtilities.UploadSettingsMetric), Type.EmptyTypes, nameof(SkipPrefix)),
        (typeof(DailyRunUtility), nameof(DailyRunUtility.UploadScore), null!, nameof(SkipTaskPrefix)),
        (typeof(StatsManager), nameof(StatsManager.IncrementArchitectDamage), new[] { typeof(int) }, nameof(SkipPrefix)),
        // Game-over badges go into the profile's character stats. That entry is only created by the
        // run-data update skipped above, so in a party run this threw KeyNotFoundException and cut the
        // game-over animation short (M2 gate, 2026-11-18). The badges still show; they are just not saved.
        (typeof(NGameOverScreen), "SaveBadgesToProgress", new[] { typeof(List<Badge>) }, nameof(SkipPrefix)),
    };

    public override string? SelfCheck()
    {
        foreach (var t in Targets)
        {
            string? missing = Require(t.Type, t.Type.Name, t.Method, t.Args);
            if (missing != null) return missing;
        }
        return null;
    }

    public override void Apply(Harmony harmony)
    {
        foreach (var t in Targets)
        {
            var original = t.Args == null ? AccessTools.Method(t.Type, t.Method) : AccessTools.Method(t.Type, t.Method, t.Args);
            harmony.Patch(original, prefix: new HarmonyMethod(typeof(ProfileGuardAdapter), t.Prefix));
        }
    }

    static bool SkipPrefix() => !SpirePartySession.IsActive;

    static bool SkipTaskPrefix(ref Task __result)
    {
        if (!SpirePartySession.IsActive) return true;
        __result = Task.CompletedTask;
        return false;
    }

    static bool UpdateWithRunDataPrefix(SerializableRun serializableRun, ref PostRunProgressUpdateResult __result)
    {
        if (!SpirePartySession.IsActive) return true;
        __result = PostRunProgressUpdateResult.Unsuccessful(serializableRun);
        return false;
    }

    /// <summary>The party folder on the host; on a client, a folder of the same name under its own SpireParty data dir.</summary>
    static string PartyDataDir(SessionContext session) =>
        session.PartyDir ?? Path.Combine(PlatformInfo.SpirePartyDataDir, "parties", SeatRules.PartyFolderName(session.PartyName));

    static bool WriteReplayPrefix(RunManager __instance, bool stopRecording)
    {
        if (SpirePartySession.Current is not { } session) return true;
        try
        {
            // The game's writer splits the path on '/', so hand it forward slashes.
            string path = Path.Combine(PartyDataDir(session), "replays", "latest.mcr")
                .Replace(Path.DirectorySeparatorChar, '/');
            __instance.CombatReplayWriter?.WriteReplay(path, stopRecording);
        }
        catch (Exception e)
        {
            Log.Warn($"{ModEntry.LogTag} could not write the combat replay to the party folder: {e.Message}");
        }
        return false;
    }

    static bool SaveRunHistoryPrefix(RunHistory history)
    {
        if (SpirePartySession.Current is not { } session) return true;
        try
        {
            string dir = Path.Combine(PartyDataDir(session), "history");
            PartyRunSave.WriteHistory(dir, history);
            Log.Info($"{ModEntry.LogTag} run history saved to the party folder ({dir})");
        }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} could not save party run history: {e}");
        }
        return false;
    }

    /// <summary>Discards any progress changes made in memory during the party session.</summary>
    public static void RestoreProgressFromDisk()
    {
        try
        {
            var result = SaveManager.Instance.InitProgressData();
            Log.Info($"{ModEntry.LogTag} progress reloaded from disk after the session ({result.Status})");
        }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} could not reload progress after the session: {e}");
        }
    }
}
