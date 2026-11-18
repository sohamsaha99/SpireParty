# Milestone 1 â€” Direct connect and seat IDs

**Goal:** a host opens a SpireParty session from the game's menu, friends join by address with a seat ID and token over the game's ENet transport, and the run is keyed by SpireParty seat player IDs instead of Steam IDs. Party runs never write to the game's profile folders.

**Windows gate:** Act 1 completed with two game processes on the Windows VM over 127.0.0.1: a host with Steam on, plus a client with `--force-steam off`. Every check in "Gate test" below passes.

**Then the Fedora checkpoint** (`docs/MILESTONE-0.md` â†’ 0.5, plus Act 1 over Tailscale with each OS as host). M2 waits for it.

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

Status 2026-11-17: 1.1–1.10 implemented (commits `605a265`, `5482010`, `45723fd` and the gate-prep commit). The gate test below is next.

### 1.1 Adapter framework and session state
- [x] `GameAdapters/` base with `SelfCheck()` and `Apply(Harmony)`; `AdapterRegistry` applies only adapters whose features can fully work and builds one readable message.
- [x] `Core/SpirePartySession`: the single flag and context all patches consult; ended when the game's net service disconnects.
- [x] Unit tests on fake adapters.

### 1.2 Settings and party store
- [x] Forgiving `config.json` and `party.json` (all design defaults); `PartyStore` with atomic writes.
- [x] Unit tests.

### 1.3 Seats and tokens
- [x] Seat ID rules, player IDs (host 1; others from 7000002, never reused), word tokens, salted SHA-256, constant-time verify, per-address rate limit.
- [x] Unit tests.

### 1.4 Control channel and handshake
- [x] TCP control channel (length-prefixed JSON, 16 KB cap, plain sockets), `hello` / `welcome` / `refuse` / `heartbeat` / `bye`.
- [x] Pure host decision, unit-tested for every refusal; loopback integration tests.

### 1.5 Host flow
- [x] **SpireParty Host** in the multiplayer submenu: pick or create a party, control channel plus `StartENetHost(port, 4)` forced even with Steam up, the game's own lobby (character select, or load lobby for a saved party run).
- [x] Changed from the plan: a new seat's token is shown once **to the joining player** (the seat's owner, as the design says), not to the host.

### 1.6 Join flow
- [x] **SpireParty Join**: address (optionally `:port`), seat ID, token; recent hosts remembered (no tokens stored); readable refusals; new token shown once.

### 1.7 Peer binding
- [x] ENet peers must match a fresh seat claim from the same IP; header sender ID must match the peer.

### 1.8 Party save redirect and profile guard
- [x] Co-op run save redirected to `parties/<party>/run.save`; progress, stats, metrics, daily score skipped; history to `parties/<party>/history/`; progress reloaded from disk at session end.
- [x] `tools/hash-profile.ps1` (`-Out`, `-Before/-After`) and `tools/hash-profile.sh` (`snapshot`, `compare`).

### 1.9 Drop detection (rejoin changed)
- [x] Heartbeat on the control channel; on a lost seat the host calls the game's `DisconnectClient`, so the game sees the drop.
- [x] **Changed from the plan:** the game has no working rejoin into a running run (see `docs/findings.md` → M1 findings). A dropped player is reported on the host with instructions; the party continues through the design's fallback: Save and Quit, reopen the party, everyone rejoins the load lobby (the current room restarts). A client joining a running run gets a readable message instead of a generic error. Live rejoin is deferred.

### 1.10 Docs and test switches
- [x] `docs/findings.md` → M1 patch sites and findings.
- [x] Test switches `--spireparty-host=<party>` and `--spireparty-join=<addr>,<seat>[,<token>]` (DevLaunch adapter), like the game's `--fastmp`.

## Gate test (Windows VM)

Close every game window first. All commands run in PowerShell in the repository folder.

**Setup**
1. Deploy the current build:
   `dotnet build -c Debug; powershell -File tools/deploy.ps1`
2. Snapshot your profile folders:
   `powershell -File tools/hash-profile.ps1 -Out spikes/logs/before.txt`

**Host and join**

3. Start the game **through Steam** (the host). Multiplayer → **SpireParty Host** → type `gate` → Create. You land in the character select.
4. Start the second game (the client, no Steam):
   `Start-Process "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\SlayTheSpire2.exe" -WorkingDirectory "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2" -ArgumentList '--force-steam','off','--clientId=1000','--log-file',"$PWD\spikes\logs\gate-client.log"`
5. In the client: Multiplayer → **SpireParty Join** → address `127.0.0.1`, seat `tester`, token empty → Join. ✔ A "Your seat token" box shows a token: write it down. ✔ Both games show two players in the character select.

**Refusals**

6. In a third game started like step 4 but with `--clientId=1001`: SpireParty Join `127.0.0.1`, seat `tester`, a wrong token. ✔ "Wrong token for seat "tester"." Close that game.

**Play**

7. Both pick a character, Ready, and play. Somewhere on the Act 1 map, check: ✔ `%APPDATA%\SlayTheSpire2\mods\SpireParty\parties\gate\run.save` exists.

**Drop and continue**

8. On the map, crash the client:
   `Get-CimInstance Win32_Process -Filter "Name='SlayTheSpire2.exe'" | Where-Object CommandLine -match 'clientId=1000' | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`
   ✔ Within about 10 seconds the host shows "tester lost connection".
9. Host: pause → **Save and Quit** → Multiplayer → SpireParty Host → **gate (run in progress)**. ✔ Load lobby.
10. Start the client again (step 4), SpireParty Join with seat `tester` **and the token**. ✔ The client is in the load lobby; both Ready; the run continues from the saved point.
11. While the run is going, try another join as seat `nobody` (step 6 setup). ✔ A readable "no seat ... not taking new players" message.

**Finish**

12. Play through the Act 1 boss.
13. Close both games. Snapshot again and compare:
    `powershell -File tools/hash-profile.ps1 -Out spikes/logs/after.txt`
    `powershell -File tools/hash-profile.ps1 -Before spikes/logs/before.txt -After spikes/logs/after.txt`
    ✔ Expected: no `current_run_mp.save` and nothing in `history/` changed; `settings.save`, `prefs.save` and `profile.save` may change from normal menu use. Send me the full output either way: I match every change against the logs, which mark when each session started and ended (a `progress.save` written from the menus outside a session is vanilla behaviour, one written during the party run is a failure).
14. Vanilla still works: start the game through Steam → Multiplayer → the normal **Host**. ✔ The Steam lobby opens as before. Back out.
15. `dotnet test` passes.

Send me: the step 13 output, any step that did not show ✔, and nothing else unless something broke (I read the logs myself).

## Gate result — Windows VM: PASSED 2026-11-17

Game v0.113.1 (d09d48e5), Steam build 26660232, SpireParty 0.0.1. The user did the gameplay; Claude ran the commands and read the logs.

| Check | Result |
| --- | --- |
| Host (Steam) creates party "gate"; client (no Steam) joins as new seat "tester" (player 7000002); token shown once | Pass |
| Wrong token refused with reason; unknown seat "nobody" refused while the run is in progress | Pass |
| Unauthorized ENet join (vanilla `--fastmp join`) disconnected by peer binding (smoke test) | Pass |
| Client crashed on the map: host detected it at once and disconnected the player in the game | Pass |
| Save and Quit, reopen the party, client rejoins the load lobby with its token, run continues | Pass |
| Played through the Act 1 boss into Act 2; run saved only to `parties/gate/run.save` (+ `.backup`) | Pass |
| Profile hash compare | **Failed on run 1, fixed, then passed twice** (below) |
| Vanilla Host button still uses Steam (SteamHost) | Pass |
| `dotnet test` | Pass (64) |

**Profile guard, three runs:**
1. Run 1 leaked `progress.save` (tutorial flags through `ProgressSaveManager.MarkFtueAsComplete`) and `replays/latest.mcr` into the modded profile during the session. Fixed in `8efa79a`.
2. Re-check 1 (Steam game as client): `progress.save` clean; one final replay was written just after the session ended. Fixed in `e6f028c`.
3. Re-check 2 (Steam game as host): **no changes in any profile folder.**

**Notes from the gate**
- The user forgot the seat token; it was reset by hand in `party.json` (backup kept). The host-side "reset token" action (design: roster screen) is needed early in M2.
- Lobby names show player IDs for non-Steam seats (cosmetic; see findings).
- Neow is not offered on a profile's first run (it unlocks through an epoch); that is game behaviour, not a SpireParty issue.

**Next:** the Fedora checkpoint (`docs/MILESTONE-0.md` → 0.5, plus Act 1 over Tailscale with each OS as host).

## Fedora checkpoint: PASSED 2026-11-17

Windows VM (v0.113.1, Steam 26660232) and Fedora 44 laptop (same version, commit, Steam build and `public-beta` branch) over Tailscale. SpireParty 0.0.1, the identical DLL on both (SHA-256 `5d3c4c34…`). On the user's request nothing was installed on the laptop: the DLL and manifest were copied into its `mods/SpireParty/` over Tailscale SSH, and logs and profile hashes were read over SSH.

| Check | Result |
| --- | --- |
| Mod loads on Linux; all 5 adapters self-check; model hash 1748280997 matches Windows | Pass |
| Windows hosts (no Steam), Linux joins over Tailscale (new seat, token), plays; Save and Quit, reopen, Linux rejoins: full save (~58 KB) sent over the tunnel | Pass; ping 35–52 ms, no loss |
| Linux hosts (Steam), Windows joins over Tailscale, plays; Save and Quit, reopen, Windows rejoins: full save (~52 KB) sent over the tunnel | Pass; max sampled loss 0.24%, no errors |
| No state divergence, exceptions or drops on either side | Pass |
| Profile hash compare, both machines, both directions | Pass: no run data written; only settings files from menu use and the game's own first-modded-launch copy |
| `tools/deploy.sh` on Linux | **Not tested**: needs the .NET SDK, which the user chose not to install on the laptop. Only matters for developing on Linux |

**Scope agreed with the user:** instead of a full Act 1 in each direction, each direction played several rooms plus a Save and Quit and reload over the tunnel. The reload sends the largest message the game has (the whole saved run), which is the real test for the tunnel's packet size. The full Act 1 was already played on the Windows gate.

**Open question 7 answered:** ENet over Tailscale works with the game's defaults. The ~50–60 KB load-join message crossed the 1280-byte tunnel in both directions, with ENet fragmenting it. No custom transport is needed.

**User observation:** on the laptop's fresh modded profile, the game's first-time tutorials appear in every party session. Tutorial "seen" flags are profile writes, and the profile guard blocks them during a party run.

**M2 may start** (DESIGN → Milestones: M2 waits for the Fedora checkpoint).
