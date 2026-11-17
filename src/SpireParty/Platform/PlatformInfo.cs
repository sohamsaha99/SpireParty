using Godot;

namespace SpireParty.Platform;

/// <summary>
/// The only place allowed to ask which OS we run on or where OS-specific folders live.
/// </summary>
public static class PlatformInfo
{
    /// <summary>Godot's OS name, e.g. "Windows" or "Linux".</summary>
    public static string OsName => OS.GetName();

    /// <summary>The game's resolved user data dir (what <c>user://</c> maps to).</summary>
    public static string UserDataDir => OS.GetUserDataDir();

    /// <summary>SpireParty's own data dir: <c>user://mods/SpireParty</c>. Never inside a profile folder.</summary>
    public static string SpirePartyDataDir => System.IO.Path.Combine(UserDataDir, "mods", "SpireParty");
}
