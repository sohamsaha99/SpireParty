using System;
using System.Collections.Concurrent;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace SpireParty.UI;

/// <summary>
/// Runs work on the game's main thread. Network callbacks arrive on background threads and must
/// never touch game or Godot objects directly. Also drives per-frame session checks.
/// Uses the SceneTree.ProcessFrame signal, so no custom Godot node type is needed.
/// </summary>
public static class MainThread
{
    static readonly ConcurrentQueue<Action> Queue = new();
    static bool _hooked;

    /// <summary>Called every frame on the main thread (after queued work).</summary>
    public static event Action? EveryFrame;

    public static void Ensure()
    {
        if (_hooked) return;
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        tree.ProcessFrame += Pump;
        _hooked = true;
    }

    public static void Post(Action action) => Queue.Enqueue(action);

    static void Pump()
    {
        while (Queue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Log.Error($"{ModEntry.LogTag} main-thread work failed: {e}"); }
        }
        try { EveryFrame?.Invoke(); }
        catch (Exception e) { Log.Error($"{ModEntry.LogTag} frame check failed: {e}"); }
    }
}
