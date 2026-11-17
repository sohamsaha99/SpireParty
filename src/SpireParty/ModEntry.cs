using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using SpireParty.Platform;

namespace SpireParty;

/// <summary>
/// Entry point. The game's ModManager calls <see cref="Init"/> after loading SpireParty.dll.
/// Later milestones: run adapter self-checks here, then apply Harmony patches.
/// Nothing in here may throw into the game.
/// </summary>
[ModInitializer(nameof(Init))]
public static class ModEntry
{
    public const string LogTag = "[SpireParty]";

    public static string Version { get; } =
        typeof(ModEntry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public static void Init()
    {
        try
        {
            ReleaseInfo? release = ReleaseInfoManager.Instance.ReleaseInfo;
            string gameBuild = release == null ? "unknown" : $"{release.Version} ({release.Commit})";
            string branch = release?.Branch ?? "unknown";

            Log.Info($"{LogTag} SpireParty {Version} loaded | game {gameBuild} | branch {branch} | " +
                     $"OS {PlatformInfo.OsName} | user data dir {PlatformInfo.UserDataDir}");
        }
        catch (Exception e)
        {
            // Never crash the game from a mod initializer.
            try { Log.Error($"{LogTag} startup failed, SpireParty disabled: {e}"); } catch { }
        }
    }
}
