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
}
