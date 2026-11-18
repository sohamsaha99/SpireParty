using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using SpireParty.GameAdapters;
using SpireParty.Platform;

namespace SpireParty;

/// <summary>
/// Entry point. The game's ModManager calls <see cref="Init"/> after loading SpireParty.dll.
/// Runs every adapter self-check, then applies only the patches whose features can fully work.
/// Nothing in here may throw into the game.
/// </summary>
[ModInitializer(nameof(Init))]
public static class ModEntry
{
    public const string LogTag = "[SpireParty]";
    public const string HarmonyId = "spireparty";

    public static string Version { get; } =
        typeof(ModEntry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    /// <summary>Which adapters and features are live; null until <see cref="Init"/> ran.</summary>
    public static AdapterReport? Adapters { get; private set; }

    /// <summary>Every patch site. Each milestone adds its adapters here.</summary>
    static IEnumerable<GameAdapter> AllAdapters() => new GameAdapter[]
    {
        new MultiplayerMenuAdapter(),
        new RunSaveAdapter(),
        new ProfileGuardAdapter(),
        new PeerBindingAdapter(),
        new DevLaunchAdapter(),
        new TutorialAdapter(),
        new NamesAdapter(),
        new UnlocksAdapter(),
        new PauseMenuAdapter(),
        new WireFormatAdapter(),
        new EnemyHealthAdapter(),
    };

    public static void Init()
    {
        try
        {
            ReleaseInfo? release = ReleaseInfoManager.Instance.ReleaseInfo;
            string gameBuild = release == null ? "unknown" : $"{release.Version} ({release.Commit})";
            string branch = release?.Branch ?? "unknown";

            Log.Info($"{LogTag} SpireParty {Version} loaded | game {gameBuild} | branch {branch} | " +
                     $"OS {PlatformInfo.OsName} | user data dir {PlatformInfo.UserDataDir}");

            var harmony = new Harmony(HarmonyId);
            Adapters = AdapterRegistry.CheckAndApply(AllAdapters(), a => a.Apply(harmony));
            Log.Info($"{LogTag} adapters applied: [{string.Join(", ", Adapters.Applied)}]");
            foreach (var (name, reason) in Adapters.Failed)
                Log.Warn($"{LogTag} adapter {name} not applied: {reason}");
            if (Adapters.DisabledMessage() is { } message)
                Log.Warn($"{LogTag} {message}");
            // Parties of 5–8 need the widened lobby format; without it, stay at the vanilla 4.
            if (!Adapters.IsFeatureEnabled(Features_.MoreThanFourPlayers))
                Core.RosterEdit.GameCap = Core.RosterEdit.VanillaCap;
            Log.Info($"{LogTag} player cap {Core.RosterEdit.GameCap}");
            UI.MainThread.Ensure();
        }
        catch (Exception e)
        {
            // Never crash the game from a mod initializer.
            try { Log.Error($"{LogTag} startup failed, SpireParty disabled: {e}"); } catch { }
        }
    }
}
