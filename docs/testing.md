# Testing SpireParty

How SpireParty is tested, and the tools for it. **Every change starts with an automated party run** (M3 task 3.14, user, 2026-11-18): build, unit tests, deploy, then a scripted party on autopilot, then the log checker and, for anything near saves, the profile hash compare. A person is asked only for what needs eyes or hands: readability, Steam's Play Modded prompt, the Join form, and playing a seat when a feature needs a human.

## The loop

```powershell
dotnet build -c Debug
dotnet test
tools/deploy.ps1                       # game must be closed
tools/hash-profile.ps1 -Out before.txt # only for runs that touch saves or progress
tools/party-test.ps1 -Run feat5 -Clients 4 -Autopilot -EnemyHp 0.15
# ... wait (see "Waiting" below) ...
tools/party-test.ps1 -Stop
tools/check-logs.ps1 -Run feat5 -Ignore "Invalid Task ID"
tools/hash-profile.ps1 -Out after.txt; tools/hash-profile.ps1 -Before before.txt -After after.txt
```

Feature tests use **5 instances** (host plus 4 clients: just past the vanilla 4). Gates use **8**. Run names are lower-case; the party folder is the run name.

## Dev switches

All switches live in the shipped DLL, in every build, so a release can be tested the same way. **None does anything unless it is on the command line.** Where they are read: `src/SpireParty/Dev/DevSwitches.cs` and `src/SpireParty/GameAdapters/DevLaunchAdapter.cs`.

| Switch | What it does |
| --- | --- |
| `--spireparty-host=<party>` | Open (or create) the party and host it, skipping the menus. |
| `--spireparty-mode=<mode>` | With `-host`: the mode of a party it creates: `standard`, `daily`, `custom`, `blitz`, `dread`. |
| `--spireparty-join=<address>,<seat>[,<token>[,<name>]]` | Join a party, skipping the Join form. |
| `--spireparty-roster=<party>,<op>,<seat>[,<character>][;…]` | Roster edits through the roster editor, backed up and refused with a reason exactly as in the menus, then go on with `-host` if also given. Ops: `bench`, `restore`, `add` (needs a character), `reset` (token), `remove`, `backup` (restore a backup by name, or `latest`). A refused edit stops the launch, so a scenario sees it at once. Replaces M2's `--spireparty-addseat`. |
| `--spireparty-saveafter=N` | Autopilot host: Save and Quit (as the pause menu does) once it has played N rooms in this process. |
| `--spireparty-light` | Small window (960x540), 15 fps cap, all audio muted. Set at runtime; no settings file is written. |
| `--spireparty-autopilot` | This game plays its own seat (see below). Acts only inside a SpireParty session. |
| `--spireparty-expect=N` | Autopilot host: ready in the lobby only once N players are in it (default 2). |
| `--spireparty-idle` | With the autopilot: in combat do nothing, so the turn timer and AFK must act. |
| `--spireparty-tokenfile=<path>` | A test seat stores its new token in this file (no popup) and joins with it next time. |
| `--spireparty-test-enemyhp=x` | Enemy HP factor (0 < x ≤ 3) for short fights. **Every process of the party needs the same value** (`party-test.ps1 -EnemyHp` passes it to all), or HP diverges. Overrides the party's enemy health setting. |
| `--spireparty-modifiers=a,b` | Autopilot host of a Custom party: tick these modifiers (type names: `DeadlyEvents`, `Flight`, `Vintage`, `Draft`, …) in the lobby. |
| `--spireparty-shots=<dir>` | Save a PNG of the game's viewport whenever a screen of interest opens (lobby, combat, rest site, treasure, shop, map, game over). |
| `--spireparty-test-diverge=N` | One client only: flip its N-th checksum of a session, so the host sees a state divergence (tests the diagnostics, task 3.12). |
| `--spireparty-killturn=N` | Autopilot host runs the console's `kill all` from turn N of a fight. **Avoid:** it can misalign checksums when it ends a fight (findings → M3). Use `-test-enemyhp` instead. |

The game's own `--force-steam off` and `--clientId=<n>` let several games run on one PC without Steam, each with its own test profile (`user://default/<n>/`).

## tools/party-test.ps1

Starts a host and N clients on this PC over 127.0.0.1, all without Steam. Processes are started through WMI, so they survive the Claude desktop app restarting. Windows are titled HOST, CLIENT C1, … Logs, tokens and screenshots go to `spikes/logs/<run>/` (gitignored).

| Parameter | |
| --- | --- |
| `-Run <name>` | Run (and party) name. |
| `-Clients N` | Number of clients (seats c1..cN). |
| `-Autopilot` | Everyone plays themselves. `-Manual 1,2` leaves those clients to a person; `-HostManual` the host. |
| `-EnemyHp x` | `--spireparty-test-enemyhp` on every process. |
| `-Mode <mode>` / `-Modifiers a,b` | Mode of a new party; modifiers for a Custom party. |
| `-Idle 2,3` | Those clients idle in combat. |
| `-PartyEnemyHealth x` / `-PartyTurnTimer s` | Write the party's own settings to party.json before hosting. |
| `-Shots` | Screenshots from the host and c1. |
| `-Continue` | Same party and seats again; clients rejoin with their saved tokens; older logs are kept as `<name>-r2.log`, … |
| `-SaveAfter N` | The autopilot host does Save and Quit after N rooms. |
| `-Roster "bench,c2;add,c5,silent"` | Roster edits on the host before it hosts (the party name is added in front of each). |
| `-Skip 2,3` | Do not start those clients (benched seats); the host then expects fewer players. |
| `-KillTurn N` | See the switch above. |
| `-NoLight` | Full-size windows. |
| `-Stop` | Close every game process. |

**Waiting.** Watch the host log in a background loop rather than sleeping: count `Moving to coordinate` (one per room), stop on `act 1 coord` (Act 2 reached) or on any `diverg` line. Each Continue adds one `hosting party "<name>"` line.

**Test profiles.** `party-test.ps1` seeds a missing test profile (`user://default/<id>/`) from a test profile with mods on (`default/1000`, else `default/1`, else any other), and turns `mod_settings.mods_enabled` on in a test profile the game wrote with mods off (test profiles only, never under `steam/`). Only if no test profile exists at all (all wiped) does the first host start skip SpireParty; start it again (`check-update.ps1` does).

**Resources.** A light client uses about 730 MB. 8 playing need about 6 GB of RAM; on the 12 GB VM close other desktop apps first. Ask whoever is at the PC not to click autopilot windows.

## Scenarios (tools/scenarios/)

`tools/scenarios/all.ps1` runs every feature scenario in a row (about 45 minutes; `-Only a,b` for a subset, `-Gate` adds `act1-8`) and prints one PASS or FAIL line each; full output in `spikes/logs/scenario-<name>.txt`. Each scenario runs end to end with no one at the keyboard and ends with the log checker and the profile hash compare. It stops at the first failed check with the reason (`PASS: <name>` at the end otherwise). Shared helpers: `tools/scenarios/lib.ps1`.

| Scenario | What it checks |
| --- | --- |
| `mode.ps1 -Mode <mode> [-Modifiers a,b]` | One mode with 5 players: new run, Save and Quit; bench c2 and add c5, Continue; restore c2, Continue. |
| `roster.ps1` | The M2 roster flows: bench (benched seat refused), token reset (seat reclaimed with a new token), restore, add, backup restore (the undone seat refused). |
| `midrun.ps1` | A new seat trying to join while the run is in progress is refused with SpireParty's reason; party.json gains no seat (task 3.13). |
| `diverge.ps1` | A forced divergence saves a diagnostics bundle on the host and the client, token hashes stripped, and the popup gets the report line (task 3.12). |
| `act1-8.ps1 [-Shots]` | 8 players through Act 1 into Act 2; a ninth client is refused as "party is full". |

## After a game update: tools/check-update.ps1

Slay the Spire 2 is in early access and updates often; every update can move a method an adapter patches. **Run `tools/check-update.ps1` after every Steam update of the game, and before playing a party** on a build it has not checked yet (Steam updates silently: if unsure, run it; on an unchanged build it takes about a minute). Game closed first. Before a release, run it with `-Full`.

```powershell
tools/check-update.ps1              # steps 1-4: a few minutes the first time on a build (decompile), then about a minute
tools/check-update.ps1 -Full        # also every feature scenario (about 45 minutes)
tools/check-update.ps1 -Full -Gate  # also act1-8
```

One line per step (`PASS`, `REVIEW` = look at it, `FAIL`, `SKIP`), a summary, then what to record. Exit code 1 on any FAIL. Full report and logs: `spikes/logs/check-update-*.txt`.

| Step | What it does |
| --- | --- |
| 1 build | Reads `release_info.json`, `sts2.runtimeconfig.json`, the `sts2.dll` SHA-256 and size, and Steam's build id and branch; compares them with `docs/game-version.md`. |
| 2 diff | Decompiles `sts2.dll` with `ilspycmd` (pinned 9.1.0.7988, installed if missing) into `decompiled/<version>-<commit>/` of the main checkout (gitignored; never commit it), then diffs, member by member, only the game types each adapter in `GameAdapters/` imports and names, against the previous decompiled build (`-Previous <dir>` to choose; the first one is `decompiled/sts2/`). Lists added, removed and changed members per adapter; `*` marks a member the adapter names (likely affected). Godot's generated glue is ignored. |
| 3 build, test | `dotnet build -c Debug` (compile errors are API breaks) and `dotnet test` (includes the tests that read game IL). |
| 4 launch | `tools/deploy.ps1`, one light host (`party-test.ps1 -Run upd -Clients 0`), checks the startup lines (every adapter applied, none "not applied", player cap), stops the game. |
| 5 scenario | With `-Full`: `tools/scenarios/all.ps1` (`-Gate` adds act1-8). |

**What to do with the result.** All PASS on an unchanged build: nothing to record. On a new build: fix any FAIL first (an adapter that fails its self-check leaves its features off and vanilla working, but the party then misses them); read every `REVIEW` member against its adapter; run `-Full`; then copy the rows the script prints into `docs/game-version.md` and add a `## Game update <version>` section to `docs/findings.md` for each affected adapter (what changed, whether SpireParty changed, fallback taken). Linux (`deploy.sh`) is checked by hand at the next cross-OS gate.

## tools/check-logs.ps1

`tools/check-logs.ps1 -Run <name> [-Ignore <pattern>...]` reads every log of the run and prints one row per process: errors, exceptions, divergence lines, adapter self-check failures, SpireParty warnings, checksums computed, last checksum id, cancelled card plays (logged alike on every peer when a target is already dead; counted apart), autopilot lines. Exit code 0 = clean. Checksum ids should match between processes that were in the whole run; a process that joined or left later differs.

## tools/hash-profile.ps1 (and .sh)

Snapshots the game's profile trees (`<user data>/steam/**`, `<user data>/default/**`) and compares two snapshots. Read-only. Proves a party run wrote nothing to a profile (hard rule 1).

## The autopilot

`src/SpireParty/Dev/Autopilot.cs`. It plays the local seat only, with what a mouse could do, so nothing is forged (hard rule 6):

- **Combat:** plays random playable cards with `CardModel.TryManualPlay` (what a mouse play does), waits for each to resolve, ends the turn with the same request as the End Turn button. Card-grid and hand-selection choices are clicked.
- **Lobbies:** every mode's lobby and load screen: picks a random character if the screen has character buttons, then Embark. The host waits for `--spireparty-expect` players.
- **Map:** a random enabled node, only after the room is done (voting earlier left events unplayed, findings → M3).
- **Events:** random safe option, Proceed when offered. **Rest site:** Heal or Smith, never Mend. **Treasure:** open, pick, proceed (our own; AutoSlay's re-opens the chest in co-op).
- **Shop:** `SyncedShopHandler`: buys random affordable items through `MerchantSynchronizer.PurchaseLocalEntry`, like a click.
- **Screens:** the game's AutoSlay screen handlers (rewards, card reward, upgrade, transform, choose a card/relic/bundle, game over, …), which only click UI.

What it cannot do: target a specific ally on purpose (it picks a random one), judge readability, play well (parties at full enemy HP often die in Act 1: use `-EnemyHp`), Mend, or any flow that needs typing.

## Known pitfalls

- **Never reuse AutoSlay code that changes state directly.** Its combat handler buffs its player with `PowerCmd.Apply`, its test card selector skips the choice sync, and its `ShopRoomHandler` buys with `OnTryPurchaseUnsynchronized`: all are local-only and diverge in co-op. The Blitz shop made the last one visible (run b5, findings → M3).
- **"A local card was played, but the card holder passed is null"** (`NCardPlayQueue`) is logged when the autopilot plays a card without dragging its hand node. Only the local card animation is affected; scenarios ignore it.
- **"Reward set … is already finished"** (`RewardsSetSynchronizer`): after a buffered reward message is handled, two paths each call `CompleteRewardsSetIfNecessary`; the second hits the guard, which only logs. Harmless, more frequent with more players; scenarios ignore it (the checksums are what count).
- **"Tried to free object … NCard back to pool … already been freed"** (`NodePool.Free`): logged by some processes while the game returns to the main menu after Save and Quit. The guard only logs; scenarios ignore it. The autopilot's Save and Quit also waits until no combat is in progress and no action is running: quitting mid card play made clients log the game's own `NullReferenceException` from the torn-down play (run mode-blitz, gate rerun).
- **Console `kill all`** is synced but can leave processes with different checksum counts when it ends a fight.
- **Hidden windows stall the party** (Linux, findings → M2): keep every game window visible.
- **Two xUnit tests that read game IL** must share the `GameIL` collection (MonoMod fails when two load at once).
- **Processes started from a Claude session** die when the desktop app restarts: `party-test.ps1` uses WMI for that reason; a Steam-launched host uses `steam://rungameid/2868840`.
- **Tokens are never logged.** Test seats use `--spireparty-tokenfile`.
