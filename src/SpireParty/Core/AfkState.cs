using System.Collections.Generic;
using System.Linq;

namespace SpireParty.Core;

/// <summary>
/// AFK auto-pass for the local player (DESIGN → Turn timer and AFK protection, step 2; combat only).
/// <list type="bullet">
/// <item>Each turn the timer ends is a strike; <c>afkStrikes</c> strikes in a row mark the seat AFK.</item>
/// <item>A turn the player ends themselves resets the strikes.</item>
/// <item>Real input (mouse or keys, in the focused game window) clears the mark and the strikes.</item>
/// </list>
/// While AFK, the player's own client ends each turn as soon as it can (no countdown). Pure: the caller
/// feeds events and reads <see cref="IsAfk"/>. Each method returns true when <see cref="IsAfk"/> changed.
/// </summary>
public sealed class AfkState
{
    public int Strikes { get; private set; }
    public bool IsAfk { get; private set; }

    public bool OnTimedOut(int limit)
    {
        Strikes++;
        if (IsAfk || Strikes < limit) return false;
        IsAfk = true;
        return true;
    }

    public bool OnEndedByPlayer()
    {
        Strikes = 0;
        return false;
    }

    public bool OnInput()
    {
        if (!IsAfk) return false;
        IsAfk = false;
        Strikes = 0;
        return true;
    }

    public void Reset()
    {
        Strikes = 0;
        IsAfk = false;
    }
}

/// <summary>
/// Which seats are AFK, as the host tells everyone (player IDs). The host keeps the list from its own
/// state and its clients' reports, and broadcasts it on every change. Main thread.
/// </summary>
public static class AfkRoster
{
    static HashSet<ulong> _afk = new();

    public static IReadOnlyCollection<ulong> PlayerIds => _afk;

    /// <summary>Host: one seat's state changed. Returns true when the list changed.</summary>
    public static bool Set(ulong playerId, bool afk) => afk ? _afk.Add(playerId) : _afk.Remove(playerId);

    /// <summary>Client: the host's list.</summary>
    public static void Replace(IEnumerable<ulong> playerIds) => _afk = playerIds.ToHashSet();

    public static void Clear() => _afk = new HashSet<ulong>();
}
