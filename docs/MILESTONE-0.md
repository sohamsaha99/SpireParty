# Milestone 0 — Research spike

**Goal:** prove the mod loads on both OSes, then answer every open question in `docs/DESIGN.md` from the decompiled game, so milestones 1 to 5 build on facts instead of guesses. No feature code yet beyond a skeleton and throwaway prototypes.

**Gate:** every open question is answered or has a chosen fallback in `docs/findings.md`, and the user has reviewed it. This is a **Windows gate** (see "Windows first" in `docs/DESIGN.md` → Milestones): everything that needs the Fedora laptop is collected in task 0.5 and runs at the Fedora checkpoint, after M1 works on the Windows VM.

**Order:** 0.1 (Windows part) → 0.2 → 0.3 → 0.4 → gate review. 0.5 waits for the Fedora checkpoint.

---

## Tasks

### 0.1 Skeleton that loads

- [ ] Create the repo layout from `CLAUDE.md`, `Directory.Build.props` with `Sts2GameDir`, the csproj referencing game assemblies (not copied), and the manifest `SpireParty.json`. Copy the manifest schema from a working STS2 mod; set `affects_gameplay: true` and no dependencies.
- [ ] `ModEntry` logs one line on load: SpireParty version, game build, branch, OS, resolved user data dir.
- [ ] `tools/deploy.ps1` and `tools/deploy.sh`.
- [x] Verify the mod loads in modded mode on the Windows VM. Record the game build in `docs/game-version.md`.
- Linux load check moved to 0.5.

### 0.2 Decompile and index

- [ ] Decompile `sts2.dll` into `decompiled/` (gitignored) with `ilspycmd -p`.
- [ ] Write a short map of the relevant areas in `docs/findings.md`: networking/transport, lobby, run save and load, player identity, combat turn flow, map voting, rewards, run end/progression, mod loader and multiplayer validation, user data paths.

### 0.3 Answer the open questions

**Done 2026-11-16; reviewed by the user 2026-11-17.** See `docs/findings.md` → Open questions answered. Question 7 is deferred to 0.5 (needs the tailnet).

For each question, record in `docs/findings.md`: classes and methods involved, what the code does, whether the design's simplest assumption holds, and the approach chosen (assumption or fallback).

| # | Question | Design assumes | Fallback |
| --- | --- | --- | --- |
| 1 | Where does the client's player ID come from, and where is it validated? | One ID provider that can be patched | Patch the lobby join message |
| 2 | Which run-state objects reference a player ID? | Only that player's own state plus transient votes | Fail closed; add a cleanup per reference |
| 3 | Can one player's state be saved and restored on its own? | Yes, self-contained object | Copy the player's subtree of the save file |
| 4 | Enemy health formula beyond 4 players? | Per-player, extends naturally | Clamp at 4-player value, then apply multiplier |
| 5 | Code that assumes at most 4 players? | A few constants and layout arrays | RMP patches, then fix screens one by one |
| 6 | Does a seat process need Steam? | No | Patch out Steam start-up in seat mode |
| 7 | ENet over a 1280-byte tunnel? | Works with defaults | Lower MTU or split large messages |
| 8 | Where are save paths resolved? | One path helper | Patch each place that opens the co-op save |
| 9 | What is written when a run ends? | A handful of save calls | Patch each; prove with folder-hash test |
| 10 | Whose unlocks decide co-op card pools? | The host's | Handshake forces one unlock setting |
| 11 | Where can Blitz pause between encounters? | After an encounter's rewards | Roster edits only between acts in Blitz |
| 12 | Can a client press its own End Turn from code? | Yes, via the method the button calls | Simulate the button press |
| 13 | When does co-op autosave? | At room entry; reload restarts the room | Rewind to last safe-point save |
| 14 | What happens on a mid-combat disconnect? | Party can save and quit | Host quits to menu, rewind and bench |
| 15 | Can a vote settle without a missing player? | No | Nudge, auto-pass and kick cover it |

Also record:

- [ ] How the vanilla ENet transport is created and how existing direct-connect mods reach it (read DirectConnectIP; read-only for sts2-lan-coop).
- [ ] The mod manifest fields and how `affects_gameplay` and the model-database hash are checked at join.
- [ ] How Godot's user data dir can be overridden per process (needed for isolated seat profiles); compare with CouchCoop's approach.
- [ ] Whether `Image.SaveJpgToBuffer` (Godot JPEG encoder) and viewport image reads are available and fast enough in the game's Godot build.

### 0.4 Prototypes (throwaway, kept in a `spikes/` branch or folder)

- [x] **Save surgery.** Done 2026-11-16 on the VM with two processes: see `docs/findings.md` → Save-surgery spike. Create a 2-player co-op save. On the Windows VM: two game processes (if the second-process spike below works), with a direct-connect mod (e.g. DirectConnectIP) temporarily installed, different player IDs, connected over 127.0.0.1; play one floor, then uninstall it. If two processes on the VM cannot make the save, move this prototype to 0.5 and use both machines. Copy the save into a scratch folder. Prove that SpireParty code can load it with the game's own serializer, remove one player, add them back, write it, and that the game loads the result. Never do this on the real profile save; work on copies.
- [x] **Second process.** On the Windows VM, launch a second game process with a separate user data dir and see whether it starts, what Steam does, and what the log says. Done 2026-11-16: works, see `docs/findings.md` → Second-process spike.
- [ ] **Frame read.** In a running game, read the viewport image and encode a JPEG every frame for 30 seconds; record CPU time per frame at 1280x720.

Do the second-process spike first: the save-surgery prototype and the M1 Windows gate both depend on it.

### 0.5 Fedora checkpoint items (deferred)

Run these at the Fedora checkpoint, together with the M1 cross-OS gate.

- [ ] Linux laptop on the same Steam branch and build as the VM (`docs/game-version.md`).
- [ ] `tools/deploy.sh --build` works; the mod loads in modded mode; record the Linux user data dir and build in `docs/game-version.md` and `docs/findings.md`.
- [ ] Open question 7 (ENet over Tailscale's 1280-byte MTU) tested across the tailnet.
- ~~Save surgery, if it could not be done on the VM alone.~~ Not needed: done on the VM.

## Out of scope for M0

Menus, handshake, roster UI, web server, timer. Do not start milestone 1 until the gate is passed.
