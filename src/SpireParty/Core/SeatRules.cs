using System;
using System.Linq;

namespace SpireParty.Core;

/// <summary>Seat IDs, player IDs and party names: pure rules, no I/O.</summary>
public static class SeatRules
{
    /// <summary>The game's ENet host always has net ID 1 (<c>ENetHost.NetId</c>), so the host seat does too.</summary>
    public const ulong HostPlayerId = 1;
    public const ulong FirstGuestPlayerId = 7_000_002;
    public const int MaxSeatIdLength = 16;
    public const int MaxPartyNameLength = 32;

    public static string Normalize(string? seatId) => (seatId ?? "").Trim().ToLowerInvariant();

    /// <summary>1–16 chars of a–z, 0–9 and '-', starting with a letter.</summary>
    public static bool IsValidSeatId(string seatId) =>
        seatId.Length is > 0 and <= MaxSeatIdLength
        && seatId[0] is >= 'a' and <= 'z'
        && seatId.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    /// <summary>Hands out the next guest player ID and advances the counter; IDs are never reused.</summary>
    public static ulong AllocatePlayerId(PartyFile party)
    {
        ulong id = Math.Max(party.NextPlayerId, FirstGuestPlayerId);
        while (party.Seats.Any(s => s.PlayerId == id)) id++;
        party.NextPlayerId = id + 1;
        return id;
    }

    /// <summary>
    /// Turns a party name into a safe, lower-case folder name ("Thursday Crew!" → "thursday-crew").
    /// Returns "" when nothing usable is left.
    /// </summary>
    public static string PartyFolderName(string? name)
    {
        var chars = (name ?? "").Trim().ToLowerInvariant()
            .Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-').ToArray();
        var collapsed = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length > MaxPartyNameLength ? collapsed[..MaxPartyNameLength].TrimEnd('-') : collapsed;
    }
}
