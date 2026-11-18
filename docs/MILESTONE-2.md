# Milestone 2 — Party saves and roster editor

**Goal:** between encounters the host can bench a seat, restore it, add a new seat and reset a lost token, from an in-game roster editor. Every edit goes through the game's own serializer, is backed up first, and is refused with a reason when it cannot be proven safe. The party keeps playing the same run afterwards.

**Windows gate:** a run on the Windows VM with up to four game processes (host plus clients over 127.0.0.1) survives a bench, a restore, an add and a token reset, and plays through a full act after them. Every check in "Gate test" below passes.

**Cross-OS gate** (required from M2 on, see `docs/DESIGN.md` → Milestones): Windows hosts with the Fedora laptop as a client, then the laptop hosts with Windows clients. M3 and M4 wait for both parts.

**Out of scope for M2:** more than 4 players and the layouts (M3), turn timer and AFK tools (M3), browser seats (M4), live rejoin into a running run (deferred since M1), a mode picker for party runs (see Decisions, D4), extra card rewards for joiners (D3).

**State at start (2026-11-17):** M0, the M1 Windows gate and the Fedora checkpoint passed (`docs/MILESTONE-1.md`). Party saves already live in `parties/<party>/run.save` and profile writes are blocked (pulled forward into M1), so the "per-party saves" part of M2 is done except backups.

---

## Key choices (from `docs/findings.md`)

| Topic | Choice | Why |
| --- | --- | --- |
| Where edits happen | On the saved run, after **Save and Quit**, before the load lobby opens. Never in a live run | Only the host writes the co-op save. The game never saves mid-combat, so every save is between encounters (room entry, after a combat win, after an Ancient). A reload restarts the room, so a new or restored player simply starts it with the others |
| How edits are made | Load `run.save` with `JsonSerializationUtility`, edit the `SerializableRun` objects, write with the same serializer (`PartyRunSave.WriteAsync`) | Proven in the save-surgery spike; the game loads and plays the result |
| Bench | Remove the player from `Players`; drop their `pre_finished_room.extra_rewards` key; keep their history and map drawings; store their `SerializablePlayer` in `benched/<seat>.json` | Spike: bench then restore is byte-identical to no edit |
| Restore and add | **Append** to `Players` (never reorder: slot order seeds RNG) and **backfill** an empty `PlayerMapPointHistoryEntry` on every past floor | `MapPointHistoryEntry.GetEntry` throws on a missing ID |
| New player | Built in-game with the game's own code (`Player.CreateForNewRun(character, unlockState, netId)` inside a `RunState` loaded from the save, then `ToSerializable`), then catch-up | Starter deck, relic, RNG and relic bag must come from the game, not be hand-written. Exact steps are task 2.1's spike |
| Catch-up | Gold raised to the active players' **highest** (`catchUp.gold`: `highest` default, `average`, `lowest`, `starter`; changed 2026-11-17 by the user so joiners can shop for their own deck). Max HP raised to the lowest active player's (`catchUp.maxHp`: `matchLowest` default, `starterOnly`); current HP = new max HP. Values are only ever raised, never lowered below the starter's. Dread: one Ascender's Bane per act entered after the first (the act index), on top of the ascension one the game adds | DESIGN → Join and leave |
| Fail closed | Before writing, scan the **whole save as JSON** for the affected player ID outside the three known places (`players[].net_id`, `extra_rewards` keys, history `player_id`; map drawings are a compressed blob it cannot read). Any other hit refuses the edit and names the JSON path | Catches references a future game patch adds, not only the ones known today |
| Minimum seats | Refuse a bench or remove that leaves fewer than two active seats. The host seat (ID 1) can never be benched or removed | `LoadRunLobby.IsAboutToBeginGame`; `RunManager.CanonicalizeSave` needs the host in the save |
| Backups | Before every edit, copy `run.save`, `party.json` and `benched/` into `backups/<yyyyMMdd-HHmmss>/`; keep `backupsKept` (default 20). The editor can restore any backup | Hard rule 10; DESIGN → Defaults |
| Roster check | No patch needed: the edit writes the new roster into the save, so `LoadRunLobby`'s `NotInSaveGame` check and `CanonicalizeSave` pass as is. The handshake already refuses benched and removed seats before ENet | DESIGN said "patch the roster check"; record in findings that it was unnecessary |
| Player cap | Active seats after an edit ≤ min(`maxPlayers`, 4) until M3 raises the cap | The game's lobby and wire format still cap at 4 |

## Decisions for the user

**Approved by the user 2026-11-17: all seven recommendations.**

| # | Question | Recommendation |
| --- | --- | --- |
| D1 | How does a token reset reach the player? | **Make the seat "open"**: reset clears its token, and the next join with that seat ID and an empty token claims it and is shown a new token, exactly like a first join in M1. Seats added in the editor start open too. The roster shows "open" until claimed. Alternative: the host sees the new token and passes it on |
| D2 | Where is the roster editor? | **SpireParty Host → pick a party → Roster** (works with or without a run in progress), plus an **Edit roster** entry in the pause menu that does Save and Quit and opens the editor. The pause entry is the last task and can slip to M3 if it fights the game's menus |
| D3 | `catchUp.cardRewards` (default 0) | **Parse and keep it, but don't implement it in M2**: log a warning if it is above 0. Implement only if joiners turn out too weak |
| D4 | Game modes (Daily, Custom, Blitz, Dread) in party runs | **Not in M2.** M2's roster logic handles Dread banes and Blitz saves (pure logic, unit-tested), but the party lobby stays Standard. A mode picker goes into M3 unless you want it here |
| D5 | Unlock setting (carried in the handshake since M1, not applied yet) | **Do it in M2** (task 2.7): every client's lobby unlock state is replaced per `party.json`, and a new player gets the same unlock state |
| D6 | Unit-test fixtures | **Commit small run saves made by us** (2- and 3-player, a few floors) in `tests/SpireParty.Tests/fixtures/`. They are our own play data, not game code or assets (hard rule 2) |
| D7 | Cross-OS gate scope | **Same as the Fedora checkpoint**: each direction does one full bench, restore and add cycle plus several rooms over Tailscale; the full act is played on the Windows gate. The design says "survive a full act" on every part, so say if you want the full act both ways |

## Tasks

Order: 2.1 first (the one real unknown), then 2.2–2.6 (testable without the game), then 2.7–2.10.

### 2.1 Spike: build a new player in-game — done 2026-11-17 (findings → M2)
- [x] In a running host (dev switch, party session only), load a fixture save into a `RunState`, create a player with `Player.CreateForNewRun`, find what the game does at run start that `CreateForNewRun` does not (`InitializeSeed` with the slot index, relic grab bag population, unlock state, `ExtraFields`), and serialize it.
- [x] Add it to a copy of the save, load it with host plus two clients, play a combat. ✔ No exceptions, no state divergence.
- [x] Write the steps and the game methods used in `docs/findings.md` → M2.

### 2.2 Roster edits on the save (pure logic) — done 2026-11-17
- [x] `Core/RosterEdit`: bench, restore, add (given a ready `SerializablePlayer`), remove, on an in-memory `SerializableRun` plus `PartyFile`. Each returns either the edited state or a refusal with a reason.
- [x] Refusals: host seat; fewer than two active seats; over the player cap; unclaimed `extra_rewards` for that player; player already in or missing from the save; seat state and save disagree. The fail-closed scan is added in 2.3.
- [x] Catch-up (gold, max HP, Dread banes) as a separate step on the new `SerializablePlayer` (`CatchUp`; banes are made in-game by `NewPlayer.Build`).
- [x] Unit tests on committed fixture saves (D6), including bench-then-restore byte-identical and backfill counts.
- Notes: an `Active` seat that joined the lobby but never entered the run can be added to the run with its own player ID. Restoring a player who was not last appends them, so later players' slots shift; the restored player's own state is unchanged (tested). Existing parties that M1 wrote with `"gold": "matchLowest"` keep that meaning (`lowest`); only new or unset parties get `highest`. Verified in-game with the dev switch: a fourth seat got the party's highest gold (109) and kept its starter max HP (80, above the lowest 70).

### 2.3 Fail-closed scan — done 2026-11-17
- [x] Walk the save's JSON (from the game serializer) and list every place the player ID appears (number, string or object key). Allow only the known paths; anything else refuses and names the path (`Core/SaveScan`). Runs on bench, remove, restore and add.
- [x] Unit tests: clean saves pass; injected unknown references (number, string, key, and a card field set to the ID) refuse every edit and change nothing.
- Note: only three paths are checkable. `map_drawings` is a compressed blob (base64 gzip) that the scan cannot read; it holds player IDs, and the game tolerates drawings of absent players, so it is left as is (findings → M2).

### 2.4 Backups — done 2026-11-17
- [x] `Core/PartyBackups`: `Take` before every edit: `backups/<yyyyMMdd-HHmmss>/` with `run.save`, `party.json`, `benched/` and `backup.json` (time, reason, roster). A backup appears whole or not at all (written to `.tmp`, then renamed). Prune to `backupsKept`. `Restore` backs up the current state first, so restoring can be undone, and works even when that safety backup prunes the one being restored.
- [x] `Core/RosterCommit.Write`: edit in memory (with the scan), then backup, write save, write `party.json`, write or delete `benched/<seat>.json`. If any write fails, the backup is put back and the reason is shown. After an edit the game's rolling `run.save.backup` is deleted so a read never falls back to a pre-edit run.
- [x] Unit tests on a temp dir, including a forced write failure (save and party file come back byte-identical).

### 2.5 Open seats and token reset (D1) — done 2026-11-17
- [x] Seat with an empty token hash = open. `HostHandshake.Decide`: the next hello for an open, active seat claims it, whatever token was typed, and returns a new token (shown once, as for a new seat); a wrong or empty token on a claimed seat is refused as today. Benched, removed, not-in-run and already-connected seats are refused before the claim, so a refusal never uses up an open seat.
- [x] The "not in the saved run" check moved from `HostSession` into `Decide` (`LobbyState.RunPlayerIds`), so it runs before anything changes. Before, a refused open seat would have kept an unseen token in memory. If `party.json` cannot be saved after a claim, the seat goes back to open.
- [x] `RosterEdit.ResetToken` clears the hash (host seat and removed seats refused); the editor commits it with `RosterCommit.Write` (backed up). Seats added in the editor start open.
- [x] Unit tests for every new handshake path. In-game: tester's token cleared on the spike party, joined with no token, got a new token, `party.json` saved it.

### 2.6 Seat display names — done 2026-11-17
- [x] Join form gets an optional **Name** field (defaults to the seat ID, remembered per recent host); the host stores it on every join where one is typed. Renaming in the editor comes with 2.8. Names are cleaned (`SeatNames.Clean`: trimmed, 24 characters, no control characters or `[` `]`).
- [x] Welcome carries every seat's name; the host broadcasts a `roster` message to connected clients whenever a seat joins or is renamed.
- [x] `NamesAdapter`: during a session, `PlatformUtil.GetPlayerNameRaw` and `GetPlayerName`, plus the Steam and Null strategies' `GetPlayerName` (in case the JIT inlines the small wrapper), return the seat's display name. Self-check on all four.
- [x] Unit tests (cleaning, table, codec, recent hosts). In-game: host plus "Alpha A" and "Bravo B" over the dev switch; the user saw the names in the lobby.

### 2.7 Unlock setting (D5) — done 2026-11-17
- [x] `UnlocksAdapter` applies `party.json`'s setting during a session on host and clients: the host's lobby player, the client's join request, local unlock lookups (character buttons) and the host's ascension limit. "everything" = every epoch and encounter, ascension 10 (Blitz 5); "mirrorHost" = the host's modded profile, read only, sent in the welcome (control frames raised to 64 KB).
- [x] The character select screen is built once and reused, and the game only ever unlocks its buttons: every open now sets each button's lock from the current unlocks, so the random button unlocks during a party and everything locks again afterwards (hard rule 8).
- [x] New players from the editor use the same unlock state (`PartyUnlocks.ForNewPlayer`).
- [x] Self-check; findings entry with patch sites. Unit tests for the wire format. In-game ("everything", fresh profiles): all characters and random pickable on host and client, host ascension up to 10, and single-player locked again after leaving (user checked). "mirrorHost" is not visibly testable here: every profile on the VM has nothing unlocked.

### 2.8 Roster editor UI (D2) — done 2026-11-17
- [x] Party screen after picking a party: **Continue** (current behaviour), **Roster**, **Backups**, Back. Creating a party still goes straight to the lobby.
- [x] Roster: one row per seat with ID, name, state (active, benched, not in run, open), character, HP and gold (from the save, or the benched file); actions Bench, Restore, Add to run, Remove (confirm), Reset token, Rename; **Add seat** (seat ID, name, character from those the party's unlocks allow). Every refusal shows its reason. Logic in `Core/PartyEditor` (unit-tested with a stand-in player builder); screens in `UI/RosterScreens`.
- [x] Without a run in progress the roster only edits `party.json` (rename, reset token, remove a seat).
- [x] Backups: list with time, the edit it preceded and the roster; Restore (confirm), itself undoable.
- [x] `--spireparty-addseat=<party>,<seat>,<character>` now goes through the editor; the spike class is gone. `PartyRunSave.Read` no longer uses the game's `FromJson` (its error logging needs the engine).
- In-game (user): bench, refused bench below two players, add with catch-up (99 gold when the party's highest was 95; 109 later), reset token, rename, restore, remove, the 4-player limit refusal. Then host plus three clients claimed their open seats with new tokens, loaded the edited 4-player run and played a room: no exceptions or divergence in any log. Restoring a backup was not exercised in-game (unit-tested).

### 2.9 Pause-menu entry (D2) — done 2026-11-17
- [x] **Edit roster** in the pause menu for the host during a party session (`PauseMenuAdapter`, a copy of the Save and Quit button): runs the game's Save and Quit (`NPauseMenu.CloseToMenu`), then, once the session has ended and the main menu is up, opens the roster editor for that party. Clients don't get it.
- In-game (user): button shown on the host only; pressing it saved, quit and opened the roster for "pause29"; the log shows the session ended first.

### 2.10 Run end after a mid-run add — done 2026-11-18 (gate step 10)
- [x] Game over (Give Up) with four players, one restored and one added mid-run: the game-over screen showed all four and the history record was written to `parties/m2gate/history/`. The game's Run History screen reads only the profile, so party runs never appear there (by design; no party-history viewer in M2).
- Fixed: `NGameOverScreen.SaveBadgesToProgress` threw `KeyNotFoundException` in every instance (the profile's character-stats entry is only created by the run-data update SpireParty skips), cutting the badge animation short. `ProfileGuardAdapter` now skips it in party sessions; a second game over showed no exception.

### 2.11 Docs — done 2026-11-17
- [x] `docs/findings.md` → M2 findings (new player, scan, names, unlocks, pause menu); `docs/DESIGN.md` updated: roster check not patched, open seats, edits on the save after Save and Quit (any save is between encounters), backups and rename in the host flow, fail-closed scan of the whole save, gold catch-up to the highest, display names. Done 2026-11-17.

## Gate test (Windows VM)

I run every command (build, deploy, launching the games, killing processes, hashes, logs). You do the clicks and the playing. The host runs with Steam (started by me with `steam://rungameid/2868840`, so it uses your modded Steam profile; click Play Modded if asked); clients run `--force-steam off --clientId=<n>` with their own `--log-file`. Client A joins through the **Join form**; the others through the dev switch.

**Setup**

1. I close every game, build, run `dotnet test`, deploy, and snapshot the profile folders.

**Play and bench**

2. You (host): Multiplayer → SpireParty Host → create `m2gate`. You (client A): Multiplayer → SpireParty Join → `127.0.0.1`, seat `alpha`, name `Alpha`, token empty. I start client B as seat `bravo`, name `Bravo`. ✔ Both get a token box. **Paste both tokens into the chat** (I start the rejoins in steps 5 and 7). ✔ The lobby shows Host, Alpha, Bravo.
3. Pick characters and play three or four rooms. ✔ Nameplates show the names. Then, host: pause → **Edit roster**. ✔ The game returns to the main menu and the roster opens by itself.
4. Roster: ✔ three active seats with character, HP and gold. **Bench** bravo. ✔ Status line confirms. **Bench** alpha. ✔ Refused: at least 2 players. Back → **Backups**: ✔ one backup, "before: bench bravo".
5. Party screen → **Continue**. I start A (alpha) and B (bravo), each with its token from step 2. ✔ A rejoins; B gets "Seat "bravo" is benched". Ready (confirm the missing player) and play two rooms with host + Alpha. Pause → **Edit roster**.

**Restore, add, reset**

6. Roster: **Restore** bravo. **Add seat** `charlie`, name `Charlie`, any character. ✔ The status line gives Charlie's gold (the party's highest) and max HP (the starter's, or the party's lowest if higher). **Reset token** on alpha. ✔ alpha and charlie show "open".
7. Continue. I start A (alpha, empty token), B (bravo, its old token) and C (charlie, empty token). ✔ A and C each get a new token box; B joins with its old token. ✔ All four in the load lobby. ✔ Bravo has the same deck, relics, HP and gold as before the bench.
8. Play through the Act 1 boss with all four. ✔ I find no state divergence or exceptions in any log.

**Backups and run end**

9. Host: pause → Edit roster → Back → **Backups**. **Restore** the one "before: restore bravo". ✔ Roster shows bravo benched and no charlie (and the run as it was then). Back to Backups, **Restore** the newest ("before restoring …"). ✔ Everything is back as after step 8.
10. Continue with all four, then host: pause → **Give Up** (or lose a fight). ✔ The game-over screen shows all four players. ✔ I find a `.run` file in `parties/m2gate/history/` and no exception in any log. (The game's Run History screen shows only profile runs, so a party run is not expected there.)

**Finish**

11. I close all games and compare the profile hashes. ✔ No run data in any profile folder (settings files may change from menu use).
12. Vanilla: I start the game through Steam; you open Multiplayer → the normal **Host**. ✔ The Steam lobby opens; single-player character select shows your own unlocks. Back out. I run `dotnet test`.

Send me both tokens (step 2) and anything that did not show ✔. I read the logs myself.

### Result (Windows VM, v0.113.1, 2026-11-18): passed

| Step | Result |
| --- | --- |
| 1 | Build, 163 tests, deploy, profile snapshot (74 files) |
| 2 | Host through Steam (the earlier `SteamAppId` direct launch lost Steam, see findings). Alpha joined through the Join form, Bravo through the dev switch; both got tokens; the lobby showed the names |
| 3–4 | Names on nameplates; Edit roster returned to the menu and opened the roster; bench bravo; bench alpha refused (minimum 2); one backup "before: bench bravo" |
| 5 | Alpha rejoined with its token; Bravo refused with "Seat "bravo" is benched"; two rooms with host + Alpha |
| 6 | Bravo restored unchanged (Regent, 36/75 HP, 191 gold, 12 cards); Charlie added (Silent) with 191 gold = the party's highest and 70 max HP = the starter's (party's lowest 66); alpha open |
| 7 | Alpha and Charlie claimed their open seats with new tokens; Bravo joined with its old token; four in the load lobby |
| 8 | **Shortened, accepted by the user:** one room and part of a combat with all four; 141 synced actions with matching checksums, no divergence. The clients then closed because the Claude desktop app updated itself and ended the processes it had started (not SpireParty; clients are now started through WMI). Steps 9–10 continued the four-player run |
| 9 | Restored "before: restore bravo" (bravo benched, no charlie), then the newest safety backup (all four back); all three clients rejoined |
| 10 | Game over with all four; history written to the party folder; badge exception found and fixed (see 2.10) |
| 11 | Profile hashes unchanged (checked again after the fix run and after step 12) |
| 12 | Vanilla through Steam: the normal Host opens the Steam lobby; character select shows the profile's own unlocks; 163 tests pass |

## Cross-OS gate

Same as the Fedora checkpoint: I copy the DLL to the laptop over Tailscale SSH and read its logs; nothing is installed there.

1. **Windows hosts**, the laptop joins as `fedora` plus one Windows client. Bench the laptop's seat, play a room, restore it, add a seat, play several rooms. ✔ No divergence; profile hashes clean on both machines.
2. **Laptop hosts** (its own roster editor), the VM joins with two clients. Same cycle. ✔ Same checks.
