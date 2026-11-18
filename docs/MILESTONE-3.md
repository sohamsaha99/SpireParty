# Milestone 3 — Eight players

**Goal:** a party of up to 8 plays one run in any game mode (Standard, Daily, Custom, Blitz, Dread), with every screen readable and nothing out of sync. In combat, an optional turn timer and AFK auto-pass keep one slow or absent player from stalling the others. Enemy health at 8 players is charted, and the host can tune it with one multiplier. Most testing runs by itself.

**Windows gate:** an 8-player run on the Windows VM (host plus seven clients over 127.0.0.1) plays through Act 1 with no state divergence, and the combat screen stays readable. Every check in "Gate test" below passes.

**Cross-OS gate:** Windows hosts with the Fedora laptop among the eight players, then the laptop hosts with the VM's clients. M4 waits for both parts.

**Out of scope for M3:** browser seats (M4), so also the browser-seat handoff. A waiting-on list (the game already shows who has not ended their turn). AFK handling outside combat: events, shops, votes and rewards wait for everyone, and the host benches a missing player with **Edit roster** (M2). Nudge, vote to kick and auto-voting are deferred past v1 (user, 2026-11-18). Also out: live rejoin into a running run, extra card rewards for joiners (M2 D3), a viewer for party run history (M2 carry-over).

**State at start (2026-11-18):** M2 passed its Windows gate and its cross-OS gate (`docs/MILESTONE-2.md`). The game is still v0.113.1 (commit d09d48e5, checked today). The VM has 12 GB RAM and 10 logical CPUs.

**Changed with this plan (user, 2026-11-18):** the player cap is **8**, not 10, everywhere: `docs/DESIGN.md`, `CLAUDE.md`, and `party.json` (`maxPlayers` default 8, allowed 2–8; an older file with 10 reads as 8 with a warning). AFK handling is combat only, and `voteToKick` is removed from `party.json`. Code from RemoveMultiplayerPlayerLimit may be reused where it fits, once each piece is understood. Game modes are required in M3.

---

## Key choices (from `docs/findings.md`)

| Topic | Choice | Why |
| --- | --- | --- |
| Raising the cap | Patch the host entry points (literal `4` in `StartENetHost`, `InitializeMultiplayerAsHost`, in every mode's screen), `StartRunLobby._maxPlayers` → `min(party.maxPlayers, 8)`, and **widen the wire format**: `StartRunLobbyPlayer.slotId` 2 bits → 3, player lists in `ClientLobbyJoinResponseMessage` and `LobbyBeginRunMessage` 3 bits → 4 (3 bits hold at most 7). Only during a SpireParty session | Findings → open question 5. Both ends run SpireParty (the game's mod-list check enforces it), so the wider format is safe; outside a session the format stays vanilla (hard rule 8) |
| Finding the other 4s | Audit first (task 3.2): every packet field that holds a player index or count, every array or scene node list sized 4, every `% 4`, per-player colours. Check the list against RMP's patches | Findings list only what the M0 survey saw; a missed 2-bit field is a desync or a crash at player 5 |
| Reusing RMP | Reuse RemoveMultiplayerPlayerLimit's layout and cap code where it fits (CC0, credited in `THIRD_PARTY_NOTICES.md`). Each piece is read, checked against our game build and the audit, and moved into our adapter structure with a self-check. Nothing is copied that I cannot explain | User, 2026-11-18. RMP promises 8 players to work cleanly, the same target |
| Enemy health | Postfix on `Creature.ScaleHpForMultiplayer` multiplies by `enemyHealthMultiplier`; the monsters that call the formula directly (ToughEgg, TestSubject, DecimillipedeSegment, BattlewornDummy) get checked. Block and power scaling unchanged | HP is computed on every peer, so the multiplier must be the host's on every client (next row) |
| Party settings on clients | The welcome carries `enemyHealthMultiplier`, `turnTimerSeconds`, `afkStrikes`, `maxPlayers` and the mode next to `unlocks`; clients never use their own `party.json` for these | Same as unlocks in M2: one value for the whole party |
| Turn timer | Runs in each player's own client, in combat only. At zero, that client makes the same call as its End Turn button: `ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(player, turn))` plus `CombatManager.OnEndedTurnLocally()`. If a mandatory choice screen is open, it waits until the choice is made, then ends the turn | Findings → open question 12; the host rebuilds the action with the sender's ID, so nothing is forged (hard rule 6). Waiting on a choice: user, 2026-11-18 |
| AFK auto-pass | `Core/AfkState` (pure, unit-tested): `afkStrikes` (default 2) timed-out turns in a row mark the seat AFK; while AFK its own client ends each turn as soon as it can act; any real input clears the mark. Combat only | DESIGN → Turn timer and AFK protection, simplified 2026-11-18 |
| Bench a stuck player | No new code: **Edit roster** in the host's pause menu (M2) is Save and Quit, bench, Continue, and the party replays the room without them. The gate tests it on a client whose process is killed mid-combat | M2 already built it |
| Game modes | The party is created through the game's own mode screens (Standard, Daily, Custom, Blitz, Dread) over ENet; the mode, and for Custom its modifiers, are stored in `party.json` and sent in the welcome. Party runs skip the daily leaderboard (done in M1) | DESIGN → Saves, progression and game modes |
| Roster cap | `RosterEdit.GameCap` 4 → 8; the cap stays `min(maxPlayers, 8)` | Lobby and wire format now carry 8 |

## Testing approach

As much as possible runs without you. You do what only a person can: judge readability, click Play Modded when Steam asks, use the Join form once, and play a client when the timer and AFK need a human.

- **Feature tests use 5 instances** (host plus four clients: just past the vanilla 4). The gates use 8.
- **Light clients:** a dev switch makes a client small (960x540), capped at 15 fps and muted, all at runtime with no settings file written. Task 3.1 measures whether 8 fit on the VM.
- **Autopilot:** a dev switch lets a client play its own seat with the game's own bot code (`Core.AutoSlay`, shipped in `sts2.dll`: combat, rewards, map, rest site, shop, events), from its own client, so nothing is forged. The host can run it too, so a whole party can play unattended. Never active without the switch.
- **Scripted sessions:** `tools/party-test.ps1` starts the host and N clients (WMI, labelled windows, a log per process), and dev switches drive the M2 flows (Save and Quit after N floors, bench, restore, add, Continue). One command replays a scenario end to end.
- **Automatic checks:** a log checker (exceptions, divergence, matching checksum counts across processes, adapter self-check failures, SpireParty warnings) and the profile hash compare run after every scenario. A dev switch saves a screenshot from the game's own viewport whenever a screen of interest opens (lobby, combat, rest site, treasure, shop, map, game over), so I can look at layouts myself and show you only the ones in doubt.
- **Unit tests** for everything pure (cap rules, settings, timer, AFK state, widened codecs where they can be built without the engine).

## Decisions for the user

**Approved by the user 2026-11-18:** D1, D3 and D4 as recommended; D2 not built.

| # | Question | Recommendation |
| --- | --- | --- |
| D1 | Autopilot | **Use the game's AutoSlay handlers** if task 3.1 shows they run inside a co-op client. If not, a minimal autopilot of our own (end turn at once, skip rewards, vote for the first map node), and the game's networked dev console (`kill`, `godmode`) keeps fights winnable for weak parties |
| D2 | Waiting-on list | **Not built** (user, 2026-11-18): the game already shows who has not ended their turn |
| D3 | Default enemy health multiplier | **Keep 1.0** for M3. I chart the formula for 1–8 players in findings; you judge it during the gate; a changed default comes back to you as a design change |
| D4 | Where the mode is chosen | **When the party is created**, through the game's own mode screen; one party = one run = one mode. Daily uses the day's seed from the day the run started (it is in the save), so a party can finish a daily on a later day |

## Tasks

Order: 3.1 (tooling decides how everything else is tested), 3.2 (the audit decides 3.3–3.4), then 3.3–3.9, with 3.10 growing as features land.

### 3.1 Test harness and autopilot — done 2026-11-18 (findings → M3)
- [x] Light client switch (`--spireparty-light`): window size, `Engine.MaxFps`, audio, set at runtime only.
- [x] `tools/party-test.ps1`: host without Steam (`--spireparty-host`) plus N clients via WMI, window labels, per-process logs and token files, `-Continue`, `-Stop`. A Steam host (gate) is still started with `steam://`.
- [x] Measure on the VM (RAM, CPU, GPU): 4 playing plus 4 idle, since the cap is still 4 until 3.3. Findings entry. Re-measure 8 playing after 3.3.
- [x] Spike: AutoSlay in a co-op client (D1). Its screen and shop handlers are reused; combat, rest site, treasure, events, map and lobby are our own (findings). `--spireparty-autopilot`, `-expect`, `-tokenfile` (dev only).
- [x] Dev console: works in modded games; networked commands are synced actions. `--spireparty-killturn=N` (host autopilot runs `kill all` from turn N).
- [x] `tools/check-logs.ps1` (exceptions, divergence, checksum counts, self-checks) and `--spireparty-shots=<dir>` screenshots.

### 3.2 Audit: everything that assumes four players — done 2026-11-18 (findings → M3)
- [x] Packet fields holding a slot index or a player count across all `INetMessage` types and `NetFullCombatState`.
- [x] UI: nodes or arrays sized 4 (`Character_1..4`), `% 4`, per-player colours (map markers, drawings, nameplates), fixed containers in the character select, load, game-over and rest-site screens.
- [x] Logic: any `Players.Count` limit, slot-indexed tables, `ScoreUtility`.
- [x] Read RMP's source (in the browser) and map each of its patches to an audit row: reuse, adapt or not needed.
- [x] Findings table: place, what breaks at 5–8, fix chosen.

### 3.3 Cap and wire format — done 2026-11-18 (findings → M3)
- [x] Cap: `HostSession.PlayerCap` and `RosterEdit.GameCap` → `min(maxPlayers, 8)` (SpireParty already makes the ENet and lobby calls itself, so no patch for Standard; Daily's literal 4 is patched in 3.9).
- [x] `WireFormatAdapter`: `StartRunLobbyPlayer.slotId` 2 → 3 bits and the `playersInLobby` lists of `ClientLobbyJoinResponseMessage` and `LobbyBeginRunMessage` 3 → 4 bits (audit rows 1–3), during a session only, on host and client alike (transpiler: the width constant becomes a call). Its self-check confirms each serializer still has the vanilla width, so a game patch that changes it disables the adapter instead of corrupting packets.
- [x] `RosterEdit.GameCap` → 8; the editor and handshake refuse a ninth seat with a reason.
- [x] Unit tests; automated run: 5 then 8 processes reach the new-run lobby and start; 8 closed and restarted (the run is saved at room entry, same as Save and Quit) all rejoin the load lobby and play on. A real Save and Quit at 8 is in the gate.

### 3.4 Screens for eight
- [ ] Must fix (audit rows 6–7): rest site seats 5–8 (RMP's approach), treasure holders beyond the largest layout (our own code; check how many layouts the scene has).
- [ ] Check with screenshots and fix if crowded (rows 10–12): combat, shop (RMP's re-grid if needed), player panels, lobby and load lists, map vote icons, ally targeting, game over.
- [ ] One adapter per area, only in a session, each with a self-check.
- [ ] Screenshots at 8 players from autopilot runs; you judge the doubtful ones.

### 3.5 Enemy health
- [ ] Chart HP for 1–8 players per act (and the act-3 boss) in findings, with block and power scaling noted.
- [ ] `EnemyHealthAdapter`: multiplier after the game's scaling, minimum 1 HP, only in a session, value from the welcome.
- [x] Test-only override, pulled into 3.3: `--spireparty-test-enemyhp=<x>` on every process (`tools/party-test.ps1 -EnemyHp`), applied by `EnemyHealthAdapter` (`kill all` can misalign checksums, findings → M3). With the real multiplier, the host sends the value in the welcome instead.
- [ ] Unit test the arithmetic; automated run: the same enemy's max HP matches in every log at multiplier 0.5 and 1.5.

### 3.6 Party settings screen and welcome
- [ ] Party screen gets **Settings**: max players, enemy health multiplier, turn timer, AFK strikes. Written to `party.json`, backed up like a roster edit, used from the next Continue.
- [ ] Welcome carries the settings and the mode; clients apply them for the session only.
- [ ] Unit tests: codec, missing keys, out-of-range values.

### 3.7 Turn timer
- [ ] `Core/TurnTimer` (pure): start, warning at 10 s, expire, pause, resume; clock passed in. Unit-tested.
- [ ] `TurnTimerAdapter`: starts when the local player can act after the enemy turn; ring on the End Turn button with the 10 s warning; at zero the client ends its own turn; waits while a mandatory choice is open. Skips dead players and turns already ended.
- [ ] Host pause and resume from the pause menu, broadcast over the control channel.
- [ ] Automated: a client with a dev switch that never ends its turn; the log shows the timer ending it.

### 3.8 AFK auto-pass
- [ ] `Core/AfkState` (pure, unit-tested): strikes, mark, clear on input, strikes reset when the player ends a turn themselves.
- [ ] While AFK: the seat's own client ends its turn as soon as it can act. Banner: "You are marked AFK — move the mouse or press a key to come back". AFK state broadcast over the control channel.
- [ ] Automated: the idle client from 3.7 is marked AFK after two timeouts and then passes at once.

### 3.9 Game modes in party runs
- [ ] Creating a party offers the mode screens (Standard, Daily, Custom, Blitz, Dread); each hosts over ENet with the party's cap. Mode and modifiers stored in `party.json`, sent in the welcome.
- [ ] Unlock setting covers the mode gates (Blitz and Dread epochs), as M2's "everything" already grants the epochs.
- [ ] Roster edits per mode: Dread joiners get the party's banes (M2 logic); Blitz edits at its safe point after rewards; Custom joiners: check whether modifiers that act at run creation need applying to a new player (findings → M2 noted they are skipped), and refuse the add with a reason if a modifier cannot be applied.
- [ ] Daily: no leaderboard upload (M1 guard), the day's seed kept.
- [ ] Automated: a 5-player autopilot run per mode through a few floors, then a bench and an add, then Continue.

### 3.10 Regression scenarios
- [ ] Dev switches for the roster flows (`--spireparty-roster=<party>,<bench|restore|add|reset>,<seat>[,<character>]`, replacing `--spireparty-addseat`; Save and Quit after N floors).
- [ ] Scenarios in `tools/scenarios/`: M2 bench, restore, add, token reset, backup restore; 8 players to the act-1 boss; each mode. Each ends with the log checker and the profile hash compare.

### 3.11 Docs
- [ ] `docs/findings.md` → M3 (audit, RMP mapping, health chart, autopilot, resource numbers, modes). `docs/DESIGN.md` updated for anything that changed. `docs/game-version.md` re-checked. `THIRD_PARTY_NOTICES.md` entry for every RMP piece reused.

## Gate test (Windows VM)

I run every command and the automated scenarios first; they must pass before you are asked to play. Then one session with you. Exact steps come before the gate; the outline:

1. **Automated (no one at the keyboard):** build, `dotnet test`, deploy, profile snapshot. Then the scenarios: 8 autopilot players from a new party to the Act 1 boss; Save and Quit and reload at 8; a ninth client refused; multiplier 0.5 halves enemy HP everywhere; timer and AFK on an idle client; each mode with 5 players plus a roster edit. ✔ The log checker finds nothing in any process; ✔ profile hashes unchanged. I look at every screenshot.
2. **With you, 8 players:** host through Steam (you click Play Modded), six autopilot clients, and one client you play, joined through the Join form. Turn timer 30 s. Play through Act 1. ✔ Combat, rest site, treasure, shop, map votes, an ally-targeted potion and the player panels are readable (your call). ✔ Let your timer run out twice: turn ends, then AFK auto-pass; move the mouse, ✔ cleared. ✔ Host pauses the timer; it pauses on every client.
3. **Stuck player:** I kill one client mid-combat. Host: pause → Edit roster → bench it → Continue. ✔ The room restarts with seven, and its rejoin is refused as benched.
4. **Finish:** ✔ the log checker and profile hashes clean; vanilla co-op through Steam still caps at 4 and uses the Steam lobby; `dotnet test` passes.

## Cross-OS gate

As in M2: I copy the DLL to the laptop over Tailscale SSH and read its logs; nothing is installed there. The laptop's game window must stay visible (a hidden window stalls the party, findings → M2).

1. **Windows hosts:** the laptop joins as one of eight (the rest VM clients, mostly autopilot). Several rooms including a combat and a map vote; one timer expiry on the laptop seat. ✔ No divergence; profile hashes clean on both machines.
2. **Laptop hosts:** seven VM clients join over Tailscale. Same checks, plus one non-Standard mode and one bench through Edit roster.
