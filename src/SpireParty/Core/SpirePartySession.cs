namespace SpireParty.Core;

public enum SessionRole { Host, Client }

/// <summary>
/// The one place that says whether a SpireParty session is running. Every patch checks
/// <see cref="IsActive"/> first and leaves vanilla behaviour alone otherwise (hard rule 8).
/// </summary>
public static class SpirePartySession
{
    public static bool IsActive => Current != null;
    public static SessionContext? Current { get; private set; }

    public static void Begin(SessionContext context) => Current = context;

    /// <summary>Called when the player returns to the main menu or the session fails.</summary>
    public static void End() => Current = null;
}

/// <param name="PartyName">Party folder name (host) or the host's party name (client).</param>
/// <param name="PartyDir">Absolute party folder on the host; null on clients.</param>
/// <param name="SeatId">The local player's seat.</param>
/// <param name="PlayerId">The local player's net ID in the run.</param>
public sealed record SessionContext(SessionRole Role, string PartyName, string? PartyDir, string SeatId, ulong PlayerId);
