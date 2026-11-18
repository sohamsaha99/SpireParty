# Third-party notices

SpireParty includes code adapted from the projects below. Each entry names the source, its license, and which SpireParty files contain the adapted code.

## RemoveMultiplayerPlayerLimit

- Source: https://github.com/Rain156/sts2-RMP-Mods (commit 7c977a1e30, 2026-05-24)
- License: CC0 1.0 Universal (public domain dedication). No attribution is required; it is given anyway.
- Used in:
  - `src/SpireParty/GameAdapters/WireFormatAdapter.cs`: the approach of widening the lobby slot and player-list fields by replacing the bit-width constant before each `PacketWriter`/`PacketReader` call (from `src/Network/SerializationPatches.cs`). Rewritten for this game build and made session-only; widths differ.
  - `src/SpireParty/GameAdapters/RestSiteAdapter.cs`: replacing the rest site's seat lookup in `NRestSiteRoom._Ready` with one that adds seats, the extra seat offsets, and copying the log props (from `src/Patches.RestSite.cs`). Changed: session-only, no index wrap-around, hover and select patches not taken.
  - `src/SpireParty/GameAdapters/TreasureAdapter.cs`: adding relic holders from the holder scene and laying them out in centred rows of four, with its spacing constants (from `src/Patches.Treasure.cs`). Rewritten for this build's layout structure; the "skip relic" feature is not taken.
  - `src/SpireParty/GameAdapters/MerchantLayoutAdapter.cs`: arranging the shop's player characters in rows, with its position constants (from `src/Patches.Merchant.cs`). Changed: session-only, rows of at most four.
