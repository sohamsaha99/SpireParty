using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using SpireParty.Core;
using SpireParty.Net;

namespace SpireParty.GameAdapters;

/// <summary>
/// While hosting a SpireParty session, the co-op run save is read from and written to the party
/// folder instead of the profile's current_run_mp.save (which is never touched). Hard rule 1.
/// Targets: RunSaveManager.SaveRun(SerializableRun, bool), LoadMultiplayerRunSave,
/// DeleteCurrentMultiplayerRun, HasMultiplayerRunSave (v0.113.1).
/// </summary>
public sealed class RunSaveAdapter : GameAdapter
{
    public override string Name => "RunSave";
    public override IReadOnlyList<string> Features => new[] { Features_.DirectConnect };

    static readonly Type T = typeof(RunSaveManager);

    public override string? SelfCheck() =>
        Require(T, nameof(RunSaveManager), nameof(RunSaveManager.SaveRun), new[] { typeof(SerializableRun), typeof(bool) })
        ?? Require(T, nameof(RunSaveManager), nameof(RunSaveManager.LoadMultiplayerRunSave), Type.EmptyTypes)
        ?? Require(T, nameof(RunSaveManager), nameof(RunSaveManager.DeleteCurrentMultiplayerRun), Type.EmptyTypes)
        ?? (AccessTools.PropertyGetter(T, nameof(RunSaveManager.HasMultiplayerRunSave)) == null
            ? "property RunSaveManager.HasMultiplayerRunSave not found" : null);

    public override void Apply(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(T, nameof(RunSaveManager.SaveRun), new[] { typeof(SerializableRun), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(RunSaveAdapter), nameof(SaveRunPrefix)));
        harmony.Patch(AccessTools.Method(T, nameof(RunSaveManager.LoadMultiplayerRunSave), Type.EmptyTypes),
            prefix: new HarmonyMethod(typeof(RunSaveAdapter), nameof(LoadPrefix)));
        harmony.Patch(AccessTools.Method(T, nameof(RunSaveManager.DeleteCurrentMultiplayerRun), Type.EmptyTypes),
            prefix: new HarmonyMethod(typeof(RunSaveAdapter), nameof(DeletePrefix)));
        harmony.Patch(AccessTools.PropertyGetter(T, nameof(RunSaveManager.HasMultiplayerRunSave)),
            prefix: new HarmonyMethod(typeof(RunSaveAdapter), nameof(HasPrefix)));
    }

    static string? PartySavePath => HostSession.Current?.RunSavePath;

    static bool SaveRunPrefix(SerializableRun save, bool isMultiplayer, ref Task __result)
    {
        if (!isMultiplayer || !SpirePartySession.IsActive) return true;
        if (PartySavePath is not { } path)
        {
            // A party client never saves; if this ever runs, skip rather than touch the profile.
            Log.Warn($"{ModEntry.LogTag} skipped a co-op save outside a SpireParty host session");
            __result = Task.CompletedTask;
            return false;
        }
        __result = PartyRunSave.WriteAsync(path, save);
        return false;
    }

    static bool LoadPrefix(ref ReadSaveResult<SerializableRun> __result)
    {
        if (!SpirePartySession.IsActive) return true;
        __result = PartySavePath is { } path
            ? PartyRunSave.Read(path)
            : new ReadSaveResult<SerializableRun>(ReadSaveStatus.FileNotFound);
        return false;
    }

    static bool DeletePrefix()
    {
        if (!SpirePartySession.IsActive) return true;
        if (PartySavePath is { } path) PartyRunSave.Delete(path);
        return false;
    }

    static bool HasPrefix(ref bool __result)
    {
        if (!SpirePartySession.IsActive) return true;
        __result = PartySavePath is { } path && PartyRunSave.Exists(path);
        return false;
    }
}

/// <summary>Feature names used by adapters.</summary>
public static class Features_
{
    /// <summary>Hosting and joining SpireParty sessions. Needs every M1 adapter: never run half-patched.</summary>
    public const string DirectConnect = "SpireParty Host and Join";
}
