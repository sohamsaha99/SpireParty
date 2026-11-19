using MegaCrit.Sts2.Core.Helpers;

namespace SpireParty.Dev;

/// <summary>
/// Test-only command-line switches (M3 task 3.1). None of them is active unless passed on the
/// command line, and the autopilot only acts during a SpireParty session.
///   --spireparty-light            small window, 15 fps cap, muted (runtime only; no settings written)
///   --spireparty-autopilot        this client plays its own seat (synced actions and UI clicks only)
///   --spireparty-idle             with --spireparty-autopilot: in combat do nothing (the turn timer has to end the turn)
///   --spireparty-shots=&lt;dir&gt;      save a PNG of the game's viewport whenever the screen changes
///   --spireparty-expect=N         with --spireparty-autopilot on the host: ready only once N players are in the lobby (default 2)
///   --spireparty-tokenfile=&lt;path&gt;  a test seat stores its new token here (no popup) and joins with it next time
///   --spireparty-test-enemyhp=x   enemy HP factor for test runs (same value on every process; tools/party-test.ps1 -EnemyHp)
///   --spireparty-modifiers=a,b    with --spireparty-autopilot on a Custom party's host: tick these modifiers in the lobby
///   --spireparty-saveafter=N      with --spireparty-autopilot on the host: Save and Quit after N rooms of this process
///   --spireparty-test-diverge=N   one client only: flip its N-th checksum, to test the divergence diagnostics (task 3.12)
///   --spireparty-chattest         once in a run: open the chat with T, type and send a line, then hide it with T (Dev/ChatTest.cs)
///   --spireparty-killturn=N      with --spireparty-autopilot on the host: from turn N of a fight, run the console's "kill all" (synced, but can
///                                 misalign checksums when it ends a fight: quick runs only, see findings → M3)
/// </summary>
public static class DevSwitches
{
    public static bool Light => CommandLineHelper.HasArg("spireparty-light");
    public static bool Autopilot => CommandLineHelper.HasArg("spireparty-autopilot");

    /// <summary>--spireparty-idle: with the autopilot, play no cards and never end a combat turn (turn timer and AFK tests).</summary>
    public static bool Idle => CommandLineHelper.HasArg("spireparty-idle");

    /// <summary>--spireparty-chattest: drive the party chat with simulated keys once in a run (Dev/ChatTest.cs).</summary>
    public static bool ChatTest => CommandLineHelper.HasArg("spireparty-chattest");

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

    /// <summary>--spireparty-saveafter=N: an autopilot host does Save and Quit once it has played N rooms in this process.</summary>
    public static int? SaveAfterRooms =>
        CommandLineHelper.TryGetValue("spireparty-saveafter", out string? n) && int.TryParse(n, out int v) && v > 0 ? v : null;

    /// <summary>--spireparty-test-diverge=N (one client only): flip this process's N-th checksum of a session (DivergenceAdapter).</summary>
    public static int? TestDiverge =>
        CommandLineHelper.TryGetValue("spireparty-test-diverge", out string? n) && int.TryParse(n, out int v) && v > 0 ? v : null;

    /// <summary>--spireparty-mode=&lt;mode&gt;: the mode of a party that --spireparty-host creates.</summary>
    public static string? Mode =>
        CommandLineHelper.TryGetValue("spireparty-mode", out string? m) ? Core.PartyModes.Parse(m) : null;

    /// <summary>
    /// --spireparty-modifiers=a,b: with --spireparty-autopilot on the host of a Custom party, tick these modifiers
    /// (type names such as DeadlyEvents, Flight) in the lobby before readying.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyList<string> Modifiers =>
        CommandLineHelper.TryGetValue("spireparty-modifiers", out string? s) && !string.IsNullOrWhiteSpace(s)
            ? s.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            : System.Array.Empty<string>();

    public static string? ShotsDir =>
        CommandLineHelper.TryGetValue("spireparty-shots", out string? dir) && !string.IsNullOrWhiteSpace(dir) ? dir : null;

    /// <summary>Starts whatever was asked for. Main thread; safe to call more than once.</summary>
    public static void StartAll()
    {
        if (Light) LightClient.Apply();
        if (Autopilot) Dev.Autopilot.Start();
        if (ShotsDir is { } dir) Screenshots.Start(dir);
        if (ChatTest) Dev.ChatTest.Start();
    }
}
