# SpireParty

A co-op mod for **Slay the Spire 2** that lets up to **8 friends** play one run together over a private network (Tailscale or NordVPN Meshnet), lets players **leave and join between encounters**, and keeps party saves **out of your Steam profile**.

> **Status: early (v0.x).** Slay the Spire 2 is in Early Access and its updates can break mods. SpireParty checks the game at startup; if something it needs has changed, it turns itself off with a message and the game keeps working normally.

## Contents

- [Features](#features)
- [What you need](#what-you-need)
- [Install](#install) ([Windows](#windows) · [macOS](#macos) · [Linux](#linux))
- [First launch](#first-launch)
- [Network setup](#network-setup)
- [Host a party](#host-a-party)
- [Join a party](#join-a-party)
- [During a run](#during-a-run)
- [Where your data lives](#where-your-data-lives)
- [Settings files](#settings-files)
- [Update or uninstall](#update-or-uninstall)
- [Troubleshooting](#troubleshooting)
- [Known limitations](#known-limitations)
- [Build from source](#build-from-source)
- [License and credits](#license-and-credits)

## Features

- **Up to 8 players** in one co-op run, up from the game's 4. The campfire, shop and treasure screens are laid out for larger parties.
- **Direct connect over a mesh VPN.** Friends join by the host's Tailscale or NordVPN Meshnet address. No Steam lobby, no port forwarding.
- **Seats, not Steam accounts.** Each player has a *seat* in the party: a short seat ID (like `ravi`) and a secret token. Any copy of the game, on any Steam account, can play a seat.
- **Leave and join mid-run.** Between encounters, the host can **bench** a player (their deck, relics, gold and HP are kept aside), **restore** them later, **add** a new player, **remove** a seat, **rename** a player or **reset** a lost token.
- **Catch-up for new players.** A player added mid-run starts with their character's starter deck and relic. Their gold is raised to the party's highest and their max HP is set to the party's lowest; both rules can be changed.
- **Backups before every roster edit**, with a Backups screen to restore any of them. A roster edit that cannot be proven safe is refused with a reason.
- **Your profile stays clean.** Party runs are saved in SpireParty's own folder. They never change your normal saves, progress, unlocks, statistics, achievements or leaderboards.
- **Game modes:** Standard (every ascension level), Custom, Daily, and Blitz and Dread where your game version has them.
- **Party settings:** an optional turn timer in combat (30 to 90 seconds) that ends a slow player's turn, AFK auto-pass for a player who keeps timing out, an enemy health multiplier, and a max player count.
- **Party chat.** Press T to send a message to everyone, in the lobby, on the map or in combat. Messages show for 10 seconds and are never saved.
- **Clear errors.** A version mismatch, wrong token or full party is refused with a readable reason instead of a generic failure.
- **Normal play is untouched.** Outside a SpireParty session the game, including its normal Steam co-op, behaves as usual.

## What you need

- **Every player needs their own copy of Slay the Spire 2** with SpireParty installed.
- **Everyone on the same game version and branch.** The game refuses players whose version differs from the host's. If the host is on the `public-beta` branch, everyone must be.
- **Everyone on the same SpireParty version.** The game checks gameplay mods by exact version.
- **A mesh VPN that connects all players:** [Tailscale](https://tailscale.com/) or [NordVPN Meshnet](https://nordvpn.com/meshnet/). See [Network setup](#network-setup).

### Tested with

| | |
| --- | --- |
| SpireParty | v0.1.0 |
| Game | Slay the Spire 2 v0.113.1, Steam `public-beta` branch (Steam build 26660232) |
| Windows | Tested: Windows 11 |
| Linux | Tested: Fedora 44, native Linux build of the game |
| macOS | **Not tested.** The mod is plain .NET code and should load, but nobody has tried it yet. Reports welcome. |

Other game versions may work. If an update changes something SpireParty relies on, the mod shows a "SpireParty unavailable" message and stays off until a new SpireParty release.

## Install

1. Download `SpireParty-<version>.zip` from the [Releases](https://github.com/sohamsaha99/SpireParty/releases) page.
2. Find the game's install folder (steps per OS below).
3. In that folder, open the `mods` folder, or create it if it is missing (all lower-case: `mods`).
4. Extract the zip into `mods`. The zip contains a `SpireParty` folder, so you end up with:

```
mods/
  SpireParty/
    SpireParty.dll
    SpireParty.json
    LICENSE
    THIRD_PARTY_NOTICES.md
```

Make sure `SpireParty.dll` is directly inside `mods/SpireParty/`, not in an extra folder such as `mods/SpireParty/SpireParty/`.

The quickest way to open the install folder on any OS: in Steam, right-click **Slay the Spire 2** → **Manage** → **Browse local files**.

### Windows

Default install folder:

```
C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\
```

So the mod goes in:

```
C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\SpireParty\
```

To extract: right-click the zip → **Extract All...** → choose the `mods` folder. If Windows asks for administrator permission to write into `Program Files`, allow it.

If your Steam library is on another drive, use **Browse local files** to find it.

### macOS

On macOS the game is an app bundle, and the `mods` folder goes **inside** it, next to the game's executable.

1. In Steam: right-click **Slay the Spire 2** → **Manage** → **Browse local files**. Finder opens the install folder. By default this is:
   ```
   ~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/
   ```
2. Right-click `SlayTheSpire2.app` → **Show Package Contents**.
3. Open `Contents/MacOS/`. Create a folder named `mods` there if it is missing.
4. Extract the zip into it, so the mod ends up at:
   ```
   SlayTheSpire2.app/Contents/MacOS/mods/SpireParty/SpireParty.dll
   ```

From Terminal, with the zip in Downloads (change the version number to match yours):

```sh
GAME="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2"
mkdir -p "$GAME/SlayTheSpire2.app/Contents/MacOS/mods"
unzip -o ~/Downloads/SpireParty-0.1.0.zip -d "$GAME/SlayTheSpire2.app/Contents/MacOS/mods"
```

If the app bundle has a different name on your machine, use the `.app` you see in the install folder. The game looks for `mods` in the folder that holds its executable.

### Linux

Default install folder for the native Linux build:

```
~/.local/share/Steam/steamapps/common/Slay the Spire 2/
```

With the Flatpak version of Steam it is usually:

```
~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2/
```

From a terminal, with the zip in Downloads (change the path and version to match yours):

```sh
GAME="$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2"
mkdir -p "$GAME/mods"
unzip -o ~/Downloads/SpireParty-0.1.0.zip -d "$GAME/mods"
```

If you play the Windows build through Proton instead, the steps are the same: put `mods/SpireParty/` in the install folder that **Browse local files** opens.

## First launch

1. Start the game through Steam. If Steam asks how to launch, choose to play **with mods**.
2. The first time the game finds a mod, it shows a warning about mods. Accept it. **The game then closes or restarts**; start it again if needed.
3. In **Multiplayer** you should now see **SpireParty Host** and **SpireParty Join**.

**About your modded profile.** When mods are on, the game itself keeps a separate "modded" profile and copies your normal saves into it the first time. With Steam Cloud on, that modded profile can look reset on the first modded launch. This is the game's own behavior, not SpireParty's. Your normal (unmodded) profile is not changed, and SpireParty never writes to either profile.

## Network setup

All players must be able to reach the host's computer on port **33771** (UDP for the game, TCP for the SpireParty handshake). SpireParty only accepts connections that come through a mesh VPN, so the host is never open to the whole internet.

### Option A: Tailscale

1. Everyone installs [Tailscale](https://tailscale.com/download) and signs in.
2. Get everyone onto the host's network, in one of two ways:
   - **Same tailnet:** the host invites friends to their tailnet.
   - **Share one machine:** in the Tailscale admin console, the host shares only their gaming PC with each friend (Machines → the PC → **Share**). Friends keep their own tailnet. You can stop sharing after the session.
3. The host finds their Tailscale IP (starts with `100.`) in the Tailscale app, or with `tailscale ip -4`. Friends can also use the host's MagicDNS name.
4. Optional but recommended: use a Tailscale access rule so friends can reach only port 33771 on the host.

### Option B: NordVPN Meshnet

1. Everyone installs NordVPN, turns on **Meshnet**, and links devices (the host invites friends, or friends invite the host).
2. On the host, for each friend's device, allow incoming traffic in the Meshnet permissions.
3. The host finds their Meshnet IP (starts with `100.`) in the NordVPN app's Meshnet tab. Friends can also use the host's Meshnet name.

Running Tailscale and NordVPN on the same computer at the same time can cause routing problems, especially on Linux. Use one of them for the session.

### Firewall

- **Windows:** the first time the game hosts, Windows may ask whether to allow it on the network. Allow it. Make sure the VPN adapter counts as a **Private** network. If friends cannot connect, check Windows Defender Firewall for an inbound rule that allows Slay the Spire 2 or port 33771 (UDP and TCP).
- **Linux:** on many distributions (Fedora included) the Tailscale interface is not filtered, so nothing is needed. If you use ufw, firewalld or nftables rules on the VPN interface, allow UDP and TCP 33771 on it.
- **macOS:** if macOS asks whether the game may accept incoming connections, allow it.

SpireParty never changes firewall settings itself.

## Host a party

A **party** is one run with one group of friends. You can have several parties saved at once.

1. Start the VPN. Then start the game → **Multiplayer** → **SpireParty Host**.
2. Type a party name (for example `thursday-crew`) and create it, or pick a saved party to continue it.
3. For a new party, pick the **game mode**. The party plays one run in that mode; it cannot be changed later.
4. The lobby opens. Send your friends:
   - your **Tailscale or Meshnet IP** (or name),
   - the **seat ID** each of them should use (or let them pick their own).
5. When everyone is in and has picked a character, start the run as usual.

To continue a party later, open **SpireParty Host** and pick it from the list. Players rejoin with their seat ID and token.

From the party screen you can also open **Roster**, **Settings**, **Backups**, and **Delete** the party. Delete moves the party to a `deleted` folder; the 10 most recent deleted parties are kept and can be brought back by hand.

## Join a party

1. Start the VPN. Then start the game → **Multiplayer** → **SpireParty Join**.
2. Fill in:
   - **Host address:** the host's Tailscale or Meshnet IP or name. Add `:port` only if the host changed the port.
   - **Seat ID:** your seat (letters, digits and dashes), for example `ravi`.
   - **Your name** (optional): shown to the party.
   - **Token:** leave it **empty the first time** you join.
3. Press **Join**.

**The first time you join, SpireParty shows your token once** (for example `amber-otter-41`). **Write it down.** You need it to rejoin the party later, and SpireParty does not store it for you. If you lose it, the host can reset your seat's token (Roster → your seat → **Reset token**); then join again with an empty token and you get a new one.

The Join screen remembers recent hosts (but not tokens). **Forget** removes an entry from that list.

## During a run

### Turn timer and AFK

If the host turned on the **turn timer**, a ring on the End Turn button counts down your turn, with a warning at 10 seconds. At zero, your own game ends your turn. Cards you already played stay played. If a card or relic is asking you to choose something, the timer waits for your choice.

A player who times out several turns in a row (2 by default) is marked **AFK, passing**: their turns end right away. Any input from that player clears the mark. The host can pause the timer for everyone from the pause menu.

Outside combat (map votes, events, shops, rewards), the game waits for everyone. If someone has left, the host benches them (below).

### Leave and join: Edit roster

Roster changes happen between encounters:

1. The host opens the pause menu → **Edit roster**. This saves the run, quits to the menu and opens the roster editor. (Or **Save and Quit**, then **SpireParty Host** → the party → **Roster**.)
2. The host makes changes:
   - **Bench:** take a player out of the run. Their character is kept and can be restored later with the same deck, relics, gold and HP.
   - **Restore:** bring a benched player back.
   - **Add seat:** add a new player, with a character. They join with catch-up gold and max HP. A new seat is "open" until someone joins with that seat ID and gets its token.
   - **Remove:** delete a seat from the run for good.
   - **Rename** or **Reset token.**
3. The host presses **Continue**, and players join again. The current room starts over with the new roster.

Notes:

- A run needs at least **2 active players**. SpireParty refuses a bench or remove that would leave fewer.
- Every edit is backed up first. **Backups** restores any earlier state, and restoring is itself backed up.
- If an edit cannot be proven safe for the save (for example, a player still has unclaimed rewards), SpireParty refuses it and says why.
- In Custom runs, a player cannot be added if a modifier sets players up at run start in a way a newcomer cannot follow (for example Draft, Insanity or Sealed Deck). SpireParty says so.
- If someone's game froze or disconnected, Edit roster plus bench is also the way out.

### Party chat

Press **T** to chat with the party, any time during a SpireParty session: in the lobby, on the map, in combat.

- **T** opens the chat: the recent messages and a text box appear at the top middle of the screen. Type, then press **Enter** to send.
- **Esc**, **Tab** or **Enter** on an empty line closes the chat without sending. Clicking elsewhere also closes it.
- When a message arrives, it shows for **10 seconds** and then hides. Press **T** while it shows to hide it at once; press T again to open the chat and reply.
- While the chat is open, scroll the message list with the mouse wheel.
- Messages are at most 200 characters. They are not saved anywhere: they disappear when the session ends.

While you type, your keys go to the chat, not to the game's hotkeys. Tab still opens the map when the chat is closed.

### Party settings

**Settings** on the party screen (host only). Changes take effect the next time the host presses Continue.

| Setting | Default | Choices |
| --- | --- | --- |
| Max players | 8 | 2 to 8 |
| Enemy health | 1 (the game's own co-op scaling) | 0.5 to 3 |
| Turn timer | Off | Off, 30, 45, 60, 90 seconds |
| AFK after this many timed-out turns in a row | 2 | 1 to 5 |

## Where your data lives

SpireParty keeps everything in its own folder inside the game's user data folder:

| OS | SpireParty data folder |
| --- | --- |
| Windows | `%APPDATA%\SlayTheSpire2\mods\SpireParty\` |
| Linux | `~/.local/share/SlayTheSpire2/mods/SpireParty/` |
| macOS | `~/Library/Application Support/SlayTheSpire2/mods/SpireParty/` (expected; untested) |

Inside it:

```
config.json                 this computer's settings
parties/<party>/
  party.json                roster, seats, mode and party settings
  run.save                  the run in progress
  benched/                  benched players
  backups/                  a backup before every roster edit
  history/                  finished runs of this party
deleted/                    deleted parties (newest 10 kept)
```

A party is not tied to anyone's Steam account. To let a friend host your party instead, copy its folder from `parties/` to the same place on their computer. Party saves are not synced by Steam Cloud, so keep your own copy if they matter to you.

## Settings files

Both files are plain JSON and can be edited by hand while the game is closed. Missing or unknown keys are fine; every key has a default.

`config.json` (this computer):

| Key | Default | Meaning |
| --- | --- | --- |
| `gamePort` | `33771` | Port to host on (UDP for the game, TCP for the handshake). |
| `listenAddress` | `"auto"` | `auto` listens on your Tailscale or Meshnet address. Or set one local IP address. |
| `backupsKept` | `20` | Roster backups kept per party. |
| `deletedPartiesKept` | `10` | Deleted parties kept. |

`party.json` holds the roster and the settings shown in the game, plus the catch-up rules for new players: `catchUp.gold` (`highest`, `average`, `lowest` or `starter`), `catchUp.maxHp` (`matchLowest` or `starterOnly`) and `catchUp.cardRewards` (extra card picks, default 0).

## Update or uninstall

**Update:** close the game, delete `mods/SpireParty/`, then install the new zip as above. Your parties are in the data folder, not in `mods`, so they are kept. Everyone in the party must update to the same version.

**Uninstall:** close the game and delete `mods/SpireParty/`. To also delete your parties, delete the SpireParty data folder (see [Where your data lives](#where-your-data-lives)).

## Troubleshooting

**SpireParty Host / Join are missing.** Check that `SpireParty.dll` is directly in `mods/SpireParty/`, that you accepted the in-game mods warning, and that the game was launched with mods.

**"SpireParty unavailable" / "SpireParty is disabled for this game build."** A game update changed something SpireParty depends on. The game works normally without it. Wait for a SpireParty update.

**A version mismatch message when joining.** Everyone needs the same game version and branch and the same SpireParty version. In Steam, check the game's **Properties → Betas** on every computer.

**"Connecting..." never finishes.**

- Check that the VPN is on for both of you. From the friend's computer, `tailscale ping <host IP>` (Tailscale) should get answers.
- Check the address and the port.
- Check the host's firewall (see [Firewall](#firewall)).
- With NordVPN Meshnet, check that the host allows incoming traffic from that friend.

**Wrong or lost token.** Ask the host to reset your seat's token, then join with an empty token. After several wrong tokens, the host makes you wait a minute before trying again.

**Everything is slow while one player's window is minimized or hidden.** The game runs slowly in a hidden window, and the whole party waits for that player. Keep the game window visible.

**Logs.** The game's log is in the `logs` folder of the game's user data folder (Windows: `%APPDATA%\SlayTheSpire2\logs\`; Linux: `~/.local/share/SlayTheSpire2/logs/`). SpireParty's lines start with `[SpireParty]`. Please attach the log when you report a problem.

## Known limitations

- Every player needs their own copy of the game. There are no browser seats.
- No Steam lobbies or Steam invites for party runs: everyone connects over Tailscale or NordVPN Meshnet. Hosting on the open internet is not supported.
- At most 8 players.
- Players can only join or leave between encounters (through Edit roster), not in the middle of a combat or event.
- Outside combat there is no timeout: the party waits for everyone, and the host benches a player who is gone.
- Party runs don't give achievements, don't submit Daily scores, and don't advance your profile's progress or unlocks.
- Finished party runs are saved in the party's `history/` folder but don't appear in the game's Run History screen.
- macOS is untested.

## Build from source

You need the .NET 9 SDK and a copy of the game (the build references the game's own assemblies).

```sh
dotnet build -c Release
dotnet test
```

Set the game folder in `Directory.Build.props.user` or with the `STS2_GAME_DIR` environment variable if it is not in the default Steam location. `tools/deploy.ps1` (Windows) and `tools/deploy.sh` (Linux) copy a build into the game's `mods` folder, and `tools/package.ps1` builds the release zip. Design notes are in [`docs/DESIGN.md`](docs/DESIGN.md).

## License and credits

SpireParty is released under the [MIT License](LICENSE).

Some layout code is adapted from [RemoveMultiplayerPlayerLimit](https://github.com/Rain156/sts2-RMP-Mods) (CC0); see [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md). Thanks also to the authors of CouchCoop, DirectConnectIP, sts2-lan-coop and Multiplayer Save Slots, whose work showed what was possible.

SpireParty is a fan-made mod and is not affiliated with or endorsed by Mega Crit. Slay the Spire is a trademark of Mega Crit. The mod contains no game files.
