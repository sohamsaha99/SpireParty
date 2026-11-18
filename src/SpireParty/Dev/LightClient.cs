using System;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace SpireParty.Dev;

/// <summary>
/// Makes a test client cheap to run many times on one PC: 960x540 window, at most 15 frames per
/// second, all audio muted. Only changes the running engine; nothing is written to settings.
/// </summary>
public static class LightClient
{
    public const int Width = 960, Height = 540, MaxFps = 15;
    static bool _applied;

    public static void Apply()
    {
        if (_applied) return;
        _applied = true;
        try
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(Width, Height));
            Engine.MaxFps = MaxFps;
            for (int bus = 0; bus < AudioServer.BusCount; bus++) AudioServer.SetBusMute(bus, true);
            Log.Info($"{ModEntry.LogTag} light client: {Width}x{Height}, {MaxFps} fps, muted");
        }
        catch (Exception e)
        {
            Log.Warn($"{ModEntry.LogTag} light client failed: {e.Message}");
        }
    }
}
