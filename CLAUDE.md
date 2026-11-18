# SpireParty — agent brief

SpireParty is a single C# mod for Slay the Spire 2 (STS2) that lets up to 8 friends play one co-op run, each from their own copy of the game or from a browser seat hosted on the host's PC, with join/leave between encounters and saves that are never tied to a Steam account.

**The spec is `docs/DESIGN.md`. Read it before writing code.** When this file and the design disagree, ask the user. Start work with `docs/MILESTONE-0.md`.

---

## Machines

| Machine | Role | Notes |
| --- | --- | --- |
| Windows VM | Main dev box, primary host, browser-seat host | Has the stronger GPU. Steam copy of STS2. |
| Linux laptop | Second host and "friend with own copy" client | Native Linux build of STS2, same Steam account. |

- Both machines are on the same Tailscale tailnet. Connect by tailnet IP (100.x.y.z), never LAN IP.
- For two-machine tests, run Steam in **offline mode** on one machine so the same account can run the game on both. Before going offline, let Steam fully update the game so both machines have the **same build and branch**.
- SpireParty must host and join on **both** OSes. Gates are passed on the Windows VM first, then twice across OSes: Windows hosts + Linux joins, then Linux hosts + Windows joins. M0 and M1 defer all Linux work to one **Fedora checkpoint** after M1's Windows gate (see `docs/DESIGN.md` → Milestones). Until then, do not ask for laptop access.

## Toolchain

- .NET 9 SDK. The game bundles .NET runtime 9.0.x and a Godot 4.5 fork (MegaDot). Target `net9.0`.
- Harmony: the game ships `0Harmony.dll`. Reference it; do not ship your own copy.
- `ilspycmd` (`dotnet tool install -g ilspycmd`) to decompile `sts2.dll`. With the .NET 9 SDK, pin `--version 9.1.0.7988` (newer releases need a newer SDK).
- Tests: xUnit (approved by the user 2026-11-17) with `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`, in `tests/` only; never shipped in the mod. Run `dotnet test`.
- Optional scaffold: `dotnet new install S2SModTemplate.Templates` then `dotnet new sts2mod`. Use it as reference for project wiring; the project layout below wins.

### Game paths

| | Windows | Linux |
| --- | --- | --- |
| Game install | `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2` (default) | `~/.local/share/Steam/steamapps/common/Slay the Spire 2` (default) |
| Game data folder (reference assemblies) | `<install>\data_sts2_windows_x86_64` | `<install>/data_sts2_linuxbsd_x86_64` |
| Mods folder | `<install>\mods\SpireParty\` | `<install>/mods/SpireParty/` |

- Put the install path in `Directory.Build.props` as `Sts2GameDir`, overridable per machine (env var `STS2_GAME_DIR` or `Directory.Build.props.user`, gitignored). Derive the data folder from the OS.
- Reference `sts2.dll`, `GodotSharp.dll`, `0Harmony.dll` from the data folder with `<Private>false</Private>` so they are never copied to output.
- **User data folder:** never hard-code it. Resolve through Godot's `user://` / `OS.GetUserDataDir()`. On startup, log the resolved user data dir, the game build, the branch and the SpireParty version.
- Before maintaining a release, re-check the game's `release_info.json`, `sts2.runtimeconfig.json` and `sts2.dll` for changes; record the build in `docs/game-version.md`.

## Repository layout

```
CLAUDE.md
Directory.Build.props
SpireParty.sln
src/SpireParty/
  SpireParty.csproj
  SpireParty.json            mod manifest (copy the exact schema from a working mod; affects_gameplay: true; no dependencies)
  ModEntry.cs                entry point; runs self-checks, then applies Harmony patches
  Core/                      settings (config.json, party.json), party store, roster logic, catch-up, AFK state machine
  Net/                       SpireParty handshake channel, heartbeat, transport interface (ENet adapter)
  Seats/                     seat launcher, frame capture, input injection (seat mode)
  Web/                       HTTP + WebSocket server on plain sockets, static file serving from web/
  Platform/                  the ONLY place with OS-specific code
  GameAdapters/              one class per Harmony patch site, each with a self-check
  UI/                        menus, roster editor, timer ring
  Dev/                       test-only dev switches: light client, autopilot, screenshots (inactive without a switch)
web/                         lobby page and seat viewer (plain HTML/JS, no build step)
tests/SpireParty.Tests/      unit tests for pure logic (no game needed)
tools/                       deploy.ps1, deploy.sh, party-test.ps1, check-logs.ps1, helper scripts
docs/                        DESIGN.md, MILESTONE-0.md, findings.md, game-version.md
decompiled/                  decompiled game code — GITIGNORED, never committed
```

## Build, deploy, run

1. `dotnet build -c Debug`
2. `tools/deploy.ps1` (Windows) or `tools/deploy.sh` (Linux): copy `SpireParty.dll`, `SpireParty.json`, `lib/`, `web/`, licenses into `<install>/mods/SpireParty/`. Close the game first.
3. Launch through Steam. Choose **Play Modded** if prompted. Check the log for the SpireParty startup line.
4. Build output must be identical on both OSes (one DLL). Only `native/` (WebRTC step, later) differs per OS.

## Hard rules

1. **Never touch the game's profile folders.** Nothing SpireParty does may read-modify-write files under the game's `steam/<SteamID>/...` profile tree (vanilla or `modded`). Party data lives only under `user://mods/SpireParty/`. During a party run, profile writes (progress, history, stats, achievements, leaderboard) are redirected or skipped.
2. **Never commit decompiled game code or game assets.** Decompile into `decompiled/` (gitignored). In docs, cite class and method names, and at most a few short lines.
3. **Licenses.** Code may be copied only from:
   - RemoveMultiplayerPlayerLimit (CC0/MIT): credit anyway.
   - CouchCoop (Apache-2.0): keep its LICENSE and NOTICE, mark changed files.
   - DirectConnectIP (CC BY-NC 4.0): attribution; project stays non-commercial.

   sts2-lan-coop and Multiplayer Save Slots are **read-only** (no confirmed license): study them, reimplement. Every copied file gets an entry in `THIRD_PARTY_NOTICES.md`.
4. **Adapters.** Every Harmony patch lives in `GameAdapters/<Area>Adapter.cs`. Each adapter has a `SelfCheck()` that verifies its target types and methods exist. If any check fails at startup, disable the affected SpireParty features, show one readable message, and leave the vanilla game working. Never crash the game.
5. **No OS checks outside `Platform/`.** Paths via `Path.Combine`; lower-case file names; no drive letters or backslashes in code.
6. **No forged actions in v1.** The host never submits game actions on another player's behalf. Timer and AFK effects run in the affected player's own client, or go through roster flows.
7. **Register no new game content** (cards, relics, etc.). It changes the model database hash used in multiplayer validation.
8. **Vanilla stays vanilla.** Outside a SpireParty session the game behaves normally, including normal co-op.
9. **Settings are forgiving.** Every key has a code default; missing or empty files work; unknown keys are ignored. Defaults are listed in `docs/DESIGN.md` → Defaults and configuration.
10. **Fail closed on saves.** Back up before any roster edit. If an edit cannot be proven safe, refuse it with a reason.

## How to work

- Follow the milestones in `docs/DESIGN.md` in order. M0 → M1 needs only M0's Windows gate. M2 waits for the Fedora checkpoint. From M2 on, do not start a milestone until the previous gate has passed on both OSes.
- When you learn something about game internals, write it in `docs/findings.md` with the game build it applies to.
- When an assumption in the design's open-questions table is wrong, use its fallback and record that in `docs/findings.md`.
- Unit-test pure logic (settings parsing, roster edits on in-memory models, catch-up rules, AFK state machine, token hashing). Game-dependent behavior is verified manually with a checklist in the PR or commit message.
- Prefer small, reviewable commits. Before a gate test, write the exact manual test steps for the user.
- Ask the user before: adding a third-party library, changing a default in the design, or copying code from any project.
