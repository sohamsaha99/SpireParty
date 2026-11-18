namespace SpireParty.Core;

/// <summary>
/// Bit widths of the game's lobby fields that hold a player slot or a player count (findings → M3,
/// four-player audit rows 1–3). Vanilla fits 4 players; during a SpireParty session they are one bit
/// wider, enough for 8. Both ends of a SpireParty session run SpireParty (the game's mod-list check
/// enforces it), so both use the wider format; outside a session nothing changes (hard rule 8).
/// </summary>
public static class WireWidths
{
    /// <summary><c>StartRunLobbyPlayer.slotId</c>: slots 0–3 in vanilla, 0–7 in a session.</summary>
    public const int VanillaSlotBits = 2, PartySlotBits = 3;

    /// <summary>Lobby player list lengths: up to 7 in vanilla, up to 15 in a session (8 needs 4 bits).</summary>
    public const int VanillaListBits = 3, PartyListBits = 4;

    /// <summary>The most players these widths carry: the SpireParty cap (DESIGN → Eight-player support).</summary>
    public const int MaxPlayers = 1 << PartySlotBits;

    // Called from the game's serializers in place of the vanilla constants (WireFormatAdapter).
    public static int SlotBits() => SpirePartySession.IsActive ? PartySlotBits : VanillaSlotBits;
    public static int ListBits() => SpirePartySession.IsActive ? PartyListBits : VanillaListBits;
}
