# Milestone 1 — Direct connect and seat IDs

**Goal:** a host opens a SpireParty session from the game's menu, friends join by address with a seat ID and token over the game's ENet transport, and the run is keyed by SpireParty seat player IDs instead of Steam IDs. Party runs never write to the game's profile folders.

**Windows gate:** Act 1 completed with two game processes on the Windows VM over 127.0.0.1: a host with Steam on, plus a client with `--force-steam off`. Every check in "Gate test" below passes.

**Then the Fedora checkpoint** (`docs/MILESTONE-0.md` → 0.5, plus Act 1 over Tailscale with each OS as host). M2 waits for it.

**Out of scope for M1:** more than 4 players (M3), the roster editor, bench, restore, backups and catch-up (M2), the unlock setting (M2; the handshake carries the field from M1 on), timer and AFK tools (M3), browser seats and the web server (M4).

---

## Key choices (from `docs/findings.md`)

| Topic | Choice | Why |
| --- | --- | --- |
| Transport | The game's own ENet host and client, forced even when Steam is running | `NetHostGameService.StartENetHost` and `ENetClientConnectionInitializer` already exist; no custom transport unless the Fedora checkpoint shows the MTU problem |
| Player IDs | Host seat is always **1** (hard-coded in `ENetHost.NetId`). Other seats get IDs from the host, starting at 7000002, never reused in a party | The client's ENet net ID is just a constructor argument, so no patch is needed to set it |
| Handshake channel | A small **TCP control channel on the game port number** (default TCP 33771, next to UDP 33771): length-prefixed JSON on plain sockets | The client must know its player ID *before* the ENet connect, because the net ID is sent in ENet's own handshake. The channel also carries the heartbeat that ENet drop detection lacks |
| Peer binding | The host disconnects any ENet peer whose net ID was not just granted to that IP by a successful hello, and drops game packets whose header sender ID differs from the peer's net ID | Closes the spoofing gap found in `NetHostGameService.OnPacketReceived` |
| Save location | During a party session the co-op run save goes to `user://mods/SpireParty/parties/<party>/run.save`, written directly and never through Steam Cloud | Hard rule 1 applies from the first real party run, so this is pulled forward from M2 |
| Profile guard | During a party session, skip progress, run history (redirected to the party's `history/`), stats, achievements, metrics and daily leaderboard writes, on host **and** clients | `RunManager.OnEnded` and `ProgressSaveManager.UpdateAfterCombatWon` run on clients too |
| Session scope | Every patch checks `SpirePartySession.IsActive` first and does nothing otherwise | Hard rule 8: vanilla co-op through the normal menus is unchanged |

## Tasks

### 1.1 Adapter framework and session state
- [ ] `GameAdapters/` base: each adapter declares its targets, has `SelfCheck()` (types and methods exist via reflection) and `Apply(Harmony)`. `ModEntry` runs every self-check, applies only the adapters whose check passed, disables the features that depend on a failed one, and shows one readable in-game message listing them.
- [ ] `Core/SpirePartySession`: the single flag and context (party, local seat, role) that all patches consult; cleared on return to the main menu.
- [ ] Unit tests: adapter dependency handling ("feature X disabled because adapter Y failed") on fake adapters.

### 1.2 Settings and party store
- [ ] `Core/Settings`: `config.json` (game port, listen address, recent hosts) and `party.json` (name, seats, next player ID, unlock setting placeholder). Forgiving parsing: defaults for every key, missing or empty file OK, unknown keys ignored (hard rule 9).
- [ ] `Core/PartyStore`: list, create and open parties under `user://mods/SpireParty/parties/<name>/`, folder names lower-case and sanitized; paths only through `Platform/`.
- [ ] Unit tests: parsing (missing, empty, partial, unknown keys, bad values), party name sanitizing.

### 1.3 Seats and tokens
- [ ] `Core/Seats`: seat ID rules (short, lower-case, unique), player ID assignment (host 1; others from 7000002, never reused), word-style tokens (`amber-otter-41`), stored as salted SHA-256 hashes, constant-time compare, failed attempts rate-limited per address.
- [ ] Unit tests: ID assignment and no reuse, token generate, hash and verify, rate limiting.

### 1.4 Control channel and handshake
- [ ] `Net/ControlChannel`: TCP server on the host (listen address from config) and client; length-prefixed JSON messages with a size cap; plain sockets, no HttpListener.
- [ ] Messages: `hello` (SpireParty version, game version and commit, seat ID, token), `welcome` (player ID, a token if the seat is new, unlock-setting placeholder), `refuse` (code plus readable reason, e.g. "host runs SpireParty 0.4.1, you run 0.4.0"), and `heartbeat`.
- [ ] Host decision logic as a pure function (version, build, seat, token, lobby-open state → welcome or refuse), unit-tested for every refusal reason.

### 1.5 Host flow
- [ ] Multiplayer menu entry **SpireParty Host** (Harmony postfix on the multiplayer submenu): pick or create a party, then start the control channel and `StartENetHost(port, 4)`, forced even when Steam is up, and open the game's own lobby. A new run goes to the character select; an existing party save goes to the load lobby, canonicalized with host ID 1.
- [ ] A new seat's token is shown once to the host, to pass on to the friend.

### 1.6 Join flow
- [ ] Multiplayer menu entry **SpireParty Join**: host address, seat ID, token (empty for a new seat); remembers recent hosts and seat IDs in `config.json` (tokens are not stored in plain text there).
- [ ] Sends `hello`; on `welcome`, joins through the game's `JoinFlow` with `ENetClientConnectionInitializer(playerId, address, port)`; on `refuse`, shows the reason. A newly issued token is shown once to the player.

### 1.7 Peer binding
- [ ] Host: ENet peers must match a fresh `welcome` (same IP and net ID within a short window), otherwise they are disconnected. Game packets whose header sender ID differs from the transport peer are dropped and logged.

### 1.8 Party save redirect and profile guard
- [ ] Redirect the co-op run save (`RunSaveManager` path, load and delete) to the party folder during a session, bypassing `CloudSaveStore`.
- [ ] Skip progress, stats, achievements, metrics and leaderboard writes during a session; send run history to `parties/<party>/history/`. List each patch site in `docs/findings.md`.
- [ ] `tools/hash-profile.ps1` and `tools/hash-profile.sh`: hash every file under the game's `steam/<SteamID>/` tree (and `default/<id>/`), so before/after can be compared.

### 1.9 Drop detection and rejoin
- [ ] Heartbeat on the control channel. When a seat's heartbeat stops, or ENet reports the peer gone, the host makes sure the game sees the disconnect: report it through the game's disconnect path, or patch the ignored ENet `Disconnect` event in `ENetHost.Update`.
- [ ] A dropped seat rejoins with the same seat ID and token through the vanilla rejoin flow (`ClientRejoinRequestMessage`), including mid-combat.

### 1.10 Docs
- [ ] `docs/findings.md`: every patch site, with the game build.
- [ ] `docs/game-version.md`: re-check the game build before the gate.

## Gate test (Windows VM)

Exact steps go in the gate commit. Checks:

1. Hash the profile folders (`tools/hash-profile.ps1`).
2. Host (Steam on, launched through Steam) → SpireParty Host → create party "gate"; a client (`--force-steam off`) → SpireParty Join `127.0.0.1`, new seat "tester" → gets a token → reaches the character select.
3. Play through the Act 1 boss.
4. Mid-act, kill the client process. The host notices within the heartbeat timeout; the client rejoins with seat "tester" and its token and continues.
5. Save and quit on the host; reopen the party; the client rejoins the load lobby; play on.
6. Refusals give readable reasons: wrong token, unknown seat while the lobby is closed, mismatched SpireParty version.
7. Hash the profile folders again: only expected files changed (settings and prefs from normal UI use); no run save, progress or history written to the profile.
8. Vanilla co-op through the normal Host button still works and still uses Steam.
9. `dotnet test` passes.
