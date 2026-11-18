using System;

namespace SpireParty.Core;

/// <summary>
/// The per-turn timer (DESIGN → Turn timer and AFK protection), as pure state: the caller passes the clock
/// (seconds) to every call. One instance per client; it times the local player's own turn only.
/// <list type="bullet">
/// <item><see cref="Start"/> when the local player can act on a new turn; <see cref="Stop"/> when the turn ends.</item>
/// <item><see cref="Pause"/>/<see cref="Resume"/> freeze and continue the countdown (the host pauses for everyone).</item>
/// <item><see cref="IsExpired"/> once the time is up; the caller ends the turn (or waits for a mandatory choice).</item>
/// </list>
/// </summary>
public sealed class TurnTimer
{
    public const double WarningSeconds = 10;

    double? _deadline;          // running: when it expires
    double? _pausedRemaining;   // paused: what was left

    public int Seconds { get; private set; }
    public bool IsRunning => _deadline.HasValue || _pausedRemaining.HasValue;
    public bool IsPaused => _pausedRemaining.HasValue;

    /// <summary>Starts a turn of <paramref name="seconds"/>; paused from the start if <paramref name="paused"/>.</summary>
    public void Start(double now, int seconds, bool paused = false)
    {
        if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        Seconds = seconds;
        if (paused) { _deadline = null; _pausedRemaining = seconds; }
        else { _deadline = now + seconds; _pausedRemaining = null; }
    }

    public void Stop()
    {
        _deadline = null;
        _pausedRemaining = null;
    }

    public void Pause(double now)
    {
        if (_deadline is not double deadline) return;
        _pausedRemaining = Math.Max(0, deadline - now);
        _deadline = null;
    }

    public void Resume(double now)
    {
        if (_pausedRemaining is not double left) return;
        _deadline = now + left;
        _pausedRemaining = null;
    }

    /// <summary>Seconds left (0 when expired or not running).</summary>
    public double Remaining(double now) =>
        _pausedRemaining ?? (_deadline is double d ? Math.Max(0, d - now) : 0);

    public bool IsWarning(double now) => IsRunning && Remaining(now) <= WarningSeconds;

    public bool IsExpired(double now) => _deadline is double d && now >= d;

    /// <summary>"0:42"-style text, rounded up so it shows 0:01 until the very end.</summary>
    public static string Format(double seconds)
    {
        int s = (int)Math.Ceiling(Math.Max(0, seconds));
        return $"{s / 60}:{s % 60:00}";
    }
}
