using MegaCrit.Sts2.Core.Helpers;

namespace SpireParty.Dev;

/// <summary>
/// Test-only command-line switches (M3 task 3.1). None of them is active unless passed on the
/// command line, and the autopilot only acts during a SpireParty session.
///   --spireparty-light            small window, 15 fps cap, muted (runtime only; no settings written)
///   --spireparty-autopilot        this client plays its own seat (synced actions and UI clicks only)
///   --spireparty-shots=&lt;dir&gt;      save a PNG of the game's viewport whenever the screen changes
///   --spireparty-expect=N         with --spireparty-autopilot on the host: ready only once N players are in the lobby (default 2)
///   --spireparty-tokenfile=&lt;path&gt;  a test seat stores its new token here (no popup) and joins with it next time
///   --spireparty-test-enemyhp=x   enemy HP factor for test runs (same value on every process; tools/party-test.ps1 -EnemyHp)
///   --spireparty-killturn=N       with --spireparty-autopilot on the host: from turn N of a fight, run the console's "kill all" (synced, but can
///                                 misalign checksums when it ends a fight: quick runs only, see findings → M3)
/// </summary>
public static class DevSwitches
{
    public static bool Light => CommandLineHelper.HasArg("spireparty-light");
    public static bool Autopilot => CommandLineHelper.HasArg("spireparty-autopilot");

    /// <summary>--spireparty-expect=N: an autopilot host readies only once N players are in the lobby.</summary>
    public static int ExpectPlayers =>
        CommandLineHelper.TryGetValue("spireparty-expect", out string? n) && int.TryParse(n, out int v) ? v : 2;

    /// <summary>--spireparty-tokenfile=&lt;path&gt;: test seats keep their token in this file instead of a popup.</summary>
    public static string? TokenFile =>
        CommandLineHelper.TryGetValue("spireparty-tokenfile", out string? p) && !string.IsNullOrWhiteSpace(p) ? p : null;

    /// <summary>Writes a new seat token to the test token file; false (show the popup) without the switch.</summary>
    public static bool SaveTestToken(string seatId, string token)
    {
        if (TokenFile is not { } path) return false;
        try
        {
            System.IO.File.WriteAllText(path, token);
            MegaCrit.Sts2.Core.Logging.Log.Info($"{ModEntry.LogTag} test token for seat \"{seatId}\" saved to the token file");
            return true;
        }
        catch (System.Exception e)
        {
            MegaCrit.Sts2.Core.Logging.Log.Warn($"{ModEntry.LogTag} cannot write token file: {e.Message}");
            return false;
        }
    }

    /// <summary>The saved test token, or "" when there is none.</summary>
    public static string ReadTestToken()
    {
        try { return TokenFile is { } p && System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p).Trim() : ""; }
        catch { return ""; }
    }

    /// <summary>--spireparty-killturn=N: an autopilot host runs the console's "kill all" from turn N of a fight.</summary>
    public static int? KillTurn =>
        CommandLineHelper.TryGetValue("spireparty-killturn", out string? n) && int.TryParse(n, out int v) && v > 0 ? v : null;

    /// <summary>
    /// --spireparty-test-enemyhp=x (0 &lt; x ≤ 3): enemy HP factor for test runs. Every process of the party
    /// must get the same value (tools/party-test.ps1 -EnemyHp passes it to all), or HP would diverge.
    /// </summary>
    public static decimal? TestEnemyHp =>
        CommandLineHelper.TryGetValue("spireparty-test-enemyhp", out string? s)
        && decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal v)
        && v > 0m && v <= 3m ? v : null;

    public static string? ShotsDir =>
        CommandLineHelper.TryGetValue("spireparty-shots", out string? dir) && !string.IsNullOrWhiteSpace(dir) ? dir : null;

    /// <summary>Starts whatever was asked for. Main thread; safe to call more than once.</summary>
    public static void StartAll()
    {
        if (Light) LightClient.Apply();
        if (Autopilot) Dev.Autopilot.Start();
        if (ShotsDir is { } dir) Screenshots.Start(dir);
    }
}
