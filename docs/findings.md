# Findings

What we learned about game internals. Each entry names the game build it applies to. Class and method names only; no copied game code.

Namespaces are shortened: `Core.` means `MegaCrit.Sts2.Core.`. Line numbers are approximate and refer to the ilspycmd output in `decompiled/`.

**Verification.** Entries marked ✔ were checked by reading the code directly. Everything else comes from a read-only survey of the decompiled code and will be re-checked when M0 task 0.3 answers each open question.

## Mod loader (v0.113.1, d09d48e5)

- **Discovery.** `MegaCrit.Sts2.Core.Modding.ModManager.Initialize` scans `<dir of executable>/mods` recursively, then `mods_STEAMTEST`, then Steam Workshop items. Launching with `--nomods` skips all of it.
- **Manifest.** `ModManifest` (JSON): `id`, `name`, `author`, `description`, `version`, `has_pck`, `has_dll`, `dependencies` (array of `{id, min_version}`; plain strings are an old format that still loads but logs an error), `affects_gameplay` (defaults to **true** when missing), `min_game_version` (optional; when missing the game logs a warning and assumes the mod is supported).
- **Assembly.** It loads `<mod folder>/<id>.dll` into the game's own `AssemblyLoadContext`, so the DLL file name must match `id` exactly.
- **Entry point.** It calls every type marked `[ModInitializer("<static method>")]`. The method must be static; an exception in it is caught and logged as a load error (`MOD_ERROR.ASSEMBLY_LOAD`). If no type has the attribute, the game calls `Harmony.PatchAll` on the assembly under the id `<author>.<id>`. SpireParty uses `[ModInitializer]`, so nothing is patched automatically.
- **First run.** Mods are skipped until the player accepts the in-game mods warning (`ModSettings.PlayerAgreedToModLoading`, saved in `settings.save` as `mods_enabled`). Accepting it restarts the game.
- **Modded profile.** The first modded launch copies the vanilla profile saves into `steam/<SteamID>/modded/...`. On this VM the Steam Cloud sync then deleted those copies ("does not exist on remote") and the game wrote a fresh, small `modded/profile1/saves/progress.save`, so the modded profile started almost empty. This is vanilla behavior (SpireParty does nothing to profiles) and the vanilla profile was not touched, but expect the modded profile to look reset on first use.

## Version info (v0.113.1)

- `MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo` reads `release_info.json` next to the executable: `Version`, `Commit`, `Date`, `Branch`, `MainAssemblyHash`.
- `ReleaseInfo.Branch` is the version tag (`v0.113.1`), **not** the Steam beta name (`public-beta`). To tell stable and beta apart in the handshake, compare version and commit, or read the Steam beta name through Steamworks. Decide in M1.

## User data dir (v0.113.1)

- `Godot.OS.GetUserDataDir()` on Windows: `C:/Users/<user>/AppData/Roaming/SlayTheSpire2` (forward slashes). Profiles live under `user://steam/<SteamID>/` (vanilla) and `user://steam/<SteamID>/modded/` (modded).

---

# Map of game areas (M0 task 0.2, v0.113.1, d09d48e5)

## Networking and transport

- **Abstraction.** The two transports share abstract classes, not one interface: `Core.Multiplayer.Transport.NetHost` and `NetClient`, implemented by `Transport.ENet.ENetHost`/`ENetClient` and `Transport.Steam.SteamHost`/`SteamClient`. Above them, `Core.Multiplayer.NetHostGameService` and `NetClientGameService` own a `NetMessageBus`, a `HandshakeManager` and a `NetQualityTracker`.
- **Hosting.** `NetHostGameService.StartENetHost(port, maxClients)` binds on `0.0.0.0`; `StartSteamHost(maxClients)` creates a FriendsOnly Steam lobby plus a P2P socket.
  - Callers: `Core.Nodes.Screens.MainMenu.NMultiplayerHostSubmenu.StartHostAsync` (new run) and `NMultiplayerSubmenu.StartHostAsync(SerializableRun)` (loaded run).
  - They pick Steam whenever Steam is initialized and `--fastmp` is not set; otherwise ENet.
  - Port **33771** is a literal at every call site. ✔
- **Joining.** `Connection.ENetClientConnectionInitializer(netId, ip, port)` → `ENetClient.ConnectToHost`. The only non-debug ENet join, `NJoinFriendScreen.FastMpJoin`, hard-codes `127.0.0.1:33771`. `Multiplayer.Game.JoinFlow.Begin` drives the join.
- **Wire format.**
  - ENet frames start with a 1-byte `ENetPacketType`. The ENet-level handshake carries only a `ulong` netId and rejects only duplicate IDs (`ENetHost.DoClientHandshake`).
  - App messages (`NetMessageBus.SerializeMessage`) are a 1-byte type ID, a `ulong` sender ID, then a bit-packed body (`Serialization.PacketWriter`).
  - Type IDs come from `Serialization.MessageTypes`/`NetTypeCache`: a sorted list of all `INetMessage` types, game and mod. A 1-byte ID means at most 256 message types in total.
- **Channels and size.**
  - Reliable messages go on channel 0, unreliable on channel 1.
  - No MTU, channel count or bandwidth is configured, so Godot's ENet defaults apply.
  - `PacketWriter` grows without limit, and there is no application-level fragmentation; ENet fragments internally.
  - The join and rejoin responses carry the whole `SerializableRun`, plus `NetFullCombatState` on rejoin, so they are large. This matters for open question 7.
- **Surprises.**
  - ✔ **`ENetHost.Update` ignores ENet's transport `Disconnect` event.** The decompiled switch is offset by one: the label shown as `Receive` is really `Disconnect`, and it just `continue`s. A client that crashes or times out without sending a disconnection packet is never reported to the game when hosting over ENet. This is the patch point for drop detection (heartbeat, AFK step 6).
  - The ENet host's NetId is hard-coded to 1.
  - `NetHostGameService.OnPacketReceived` trusts the sender ID written in the packet over the transport peer, so a client could spoof another player's ID. SpireParty's handshake should bind peer to seat.

## Lobby, version and mod validation

- **Lobby classes.** All in `Core.Multiplayer.Game.Lobby`: `StartRunLobby` (new run), `LoadRunLobby` (saved run), `RunLobby` (in-run reconnects and abandon).
- **Handshake** (`Core.Multiplayer.Connection.HandshakeManager`). The host sends the magic bytes "STS2" plus `PeerVersionInfo`; the client validates it and answers with its own. `TryReadHandshakeMessage` checks, in this order:
  1. The game version string must match exactly.
  2. The gameplay-affecting mod lists must match, as `id-version` entries; otherwise `ModMismatch`.
  3. `idDatabaseHash` must match; otherwise reported as a version mismatch.
  4. A mismatch in non-gameplay mods only logs a warning.
  - The timeout is 10 s.
- **Mod lists.** `PeerVersionInfo.LocalDefault()` uses `ModManager.GetGameplayRelevantModNameList`. SpireParty (`affects_gameplay: true`) will therefore be checked by id and exact version: a vanilla client cannot join a SpireParty host, and versions must match.
- **Model hash.** `ModelIdSerializationCache.Init` takes an XxHash32 of the model database and excludes non-gameplay mods. The vanilla value logged on this build is 1748280997.
- **Join messages** (`Core.Multiplayer.Messages.Lobby`).
  1. The host sends `InitialGameInfoMessage{sessionState}`.
  2. The client answers with `ClientLobbyJoinRequestMessage`, `ClientLoadJoinRequestMessage` or `ClientRejoinRequestMessage`.
  3. The host replies with the matching response and broadcasts `PlayerJoined`, `PlayerReconnected` or `PlayerRejoined`.
  - A client that doesn't answer within 10 s is kicked.
- **The 4-player cap, everywhere it lives.**
  - ✔ The literal `4` in `StartSteamHost(4)`, `StartENetHost(33771, 4)` and `InitializeMultiplayerAsHost(..., 4)` (`NMultiplayerHostSubmenu`, `NMultiplayerSubmenu`, `NDailyRunScreen`).
  - `StartRunLobby._maxPlayers` is checked in `OnConnectedToClientAsHost`, `HandleClientLobbyJoinRequestMessage` and `TryAddPlayerInFirstAvailableSlot`.
  - ✔ **Wire format:** `Core.Entities.Multiplayer.StartRunLobbyPlayer.slotId` is written in **2 bits**, so a hard maximum of 4.
  - The player lists in `ClientLobbyJoinResponseMessage` and `LobbyBeginRunMessage` use a **3-bit** length, so at most 7. `ClientLoadJoinResponseMessage` uses 8 bits.
  - Ten players therefore needs serialization patches, not just a constant change. Both ends run SpireParty, so the wider format is safe.
  - `LoadRunLobby` has no cap of its own.

## Player identity

- **Where the NetId comes from.**
  - Steam: the SteamID, on host and client.
  - ENet: the host is 1; the client uses whatever ID it was constructed with (1000 by default, or `--clientId=N`).
  - `PlatformUtil.GetLocalPlayerId` returns the SteamID under Steam; under `NullPlatformUtilStrategy` it returns 1 or `--clientId`. Save canonicalization uses this ID.
- **Validation.** Steam checks lobby membership; ENet only rejects duplicate IDs. Nothing else is authenticated.
- **Slots.** `StartRunLobby.TryAddPlayerInFirstAvailableSlot` gives each joiner the lowest free slot. `GeneratePlayers` creates players in **list order** (not slot order), and per-player RNG is seeded from that order.
- **Roster check on load.**
  - `RunManager.CanonicalizeSave(save, localPlayerId)` throws if the host's own ID is not in `save.Players`.
  - `LoadRunLobby` rejects any client whose ID is not in the save, with `NotInSaveGame`.
  - Missing players do not block a load: `NMultiplayerLoadGameScreen.ShouldAllowRunToBegin` only shows a confirmation popup.
  - Saves made over ENet (IDs 1, 1000, …) do not match saves made over Steam (SteamIDs), and the reverse.

## Run save and load

- **Data.**
  - `Core.Saves.SerializableRun` holds acts and map, modifiers, act index, `pre_finished_room`, odds, the shared relic grab bag, `players`, rng, map point history, times, ascension, `num_reloads`, `map_drawings`, `game_mode` and `extra_fields`.
  - `Core.Saves.Runs.SerializablePlayer` holds the character, HP, gold, energy, slots, `net_id`, deck, relics, potions, per-player rng and odds, the relic grab bag, `unlock_state` and discovered lists.
  - Both also implement `IPacketSerializable`; the binary form is used to send the save to clients.
- **Serializer.**
  - System.Text.Json with source generation (`MegaCritSerializerContext`); properties are alphabetized by `JsonSerializationUtility.Options`.
  - Any type SpireParty wants inside the save must already be known to that context, so SpireParty's own data goes in its own files (`party.json`, `benched/`), not in the save.
- **Writer.** `Core.Saves.Managers.RunSaveManager.SaveRun` only writes when `RunManager.ShouldSave` is true **and** the game is single-player or host. The host writes `current_run_mp.save`. **Clients never write a run save**, so only the host can resume a co-op run.
  - Load: `LoadMultiplayerRunSave`, `LoadAndCanonicalizeMultiplayerRunSave`.
  - Delete: `DeleteCurrentMultiplayerRun` (also deletes `.backup`).
  - Snapshot: `RunManager.ToSave`.
- **When it autosaves** (single-player and co-op host alike):
  - on room entry (`RunManager.EnterMapPointInternal` with `saveGame: true`), after the old room is left and before the new room is created;
  - after a combat victory (`CombatManager`: `room.MarkPreFinished()`, then save), so the rewards state is saved;
  - when an Ancient event finishes;
  - and on load (`SaveManager.IncrementNumReloads`).
  - **Never mid-combat.**
- **What a reload restores.** `NGame.LoadRun` → `RunManager.LoadIntoLatestMapCoord` re-enters the last map point. A pre-finished room comes back in its rewards state; anything else is rebuilt from the saved RNG, so a combat starts again from the beginning.
- **File writes.** `GodotFileIo.WriteFile` writes `.tmp`, renames it, and keeps a `.backup`. `CleanupStaleCurrentRunSaves` deletes a run save whose start time already exists in history.

## Player references in the run state

- `SerializablePlayer.NetId` (`ulong`) is the key. Other saved references to a player ID:
  - `SerializableRoom.ExtraRewards` (a dictionary keyed by player ID, inside `pre_finished_room`);
  - `PlayerMapPointHistoryEntry.PlayerId`, inside `map_point_history`. `GetEntry(id)` **throws** when the ID is missing;
  - `SerializablePlayerMapDrawings.playerId`;
  - `RunHistoryPlayer.Id`, in `.run` history files.
- Not saved: map votes, reward and event synchronizer state, and cross-player references in relics or cards (no `[SavedProperty]` holds a player).
- One player's state is **mostly** self-contained (`SerializablePlayer`, rebuilt by `Player.FromSerializable`). The exceptions are their slices of `map_point_history`, `pre_finished_room.extra_rewards` and `map_drawings`, plus the shared grab bag and run RNG.

## Save paths and cloud

- **One path helper:** `Core.Saves.UserDataPathProvider`.
  - `GetAccountScopedBasePath` → `user://{steam|default|editor}/{localPlayerId}`.
  - `GetAccountDir` → `""` or `"modded"`, from the static `IsRunningModded`.
  - `GetProfileDir` → `[modded/]profile{N}`.
- **Per-file relative paths:** `RunSaveManager.GetRunSavePath`, `ProgressSaveManager.GetProgressPathForProfile`, `PrefsSaveManager.GetPrefsPath`, `RunHistorySaveManager.GetHistoryPath`, `ProfileSaveManager.GetProfileSavePath`.
- **Cloud sync.**
  - Under Steam, `SaveManager.ConstructDefault` wraps local files in `CloudSaveStore(local, SteamRemoteSaveStore)`.
  - Cloud keys are the relative paths without the SteamID.
  - `.backup` and `.pre-repair` files are never uploaded.
  - Profile, progress, both run saves, prefs and history are synced for profiles 1–3.
  - `settings.save` stays local, at the account root.
- **SpireParty consequence.** Redirecting the co-op save into `user://mods/SpireParty/...` must bypass `CloudSaveStore`, so party saves are never uploaded under a profile's cloud key.

## Run end and progression

`RunManager.OnEnded(bool)` runs when a run is won, lost or abandoned. It runs on clients too, because `ShouldSave` is true for them. Inside one save batch it:

1. updates progress through `SaveManager.UpdateProgressWithRunData` → `ProgressSaveManager.UpdateWithRunData`: playtime, character stats, streaks, ascension (`Increment{Singleplayer,Multiplayer,Blitz}Ascension`), Dread streaks, card and encounter stats and epochs, then writes `progress.save`;
2. calls `AchievementsHelper.AfterRunEnded`, an empty stub in this build;
3. writes `history/{StartTime}.run` through `RunHistoryUtilities.CreateRunHistoryEntry` → `RunHistorySaveManager.SaveHistory`;
4. uploads run metrics (`MetricUtilities.UploadRunMetrics`, network);
5. deletes the run save (single-player or host);
6. on a win, calls `StatsManager.IncrementArchitectDamage` (a Steam stat);
7. for a daily run, calls `DailyRunUtility.UploadScore`.

Other writers:

- `ProgressSaveManager.UpdateAfterCombatWon`, mid-run after each combat;
- abandoning from the main menu (`NMainMenu.AbandonRun`, `NMultiplayerSubmenu`), which repeats steps 1, 3 and 7;
- `NGameOverScreen`, which saves progress again.

For open question 9, every one of these needs a redirect or skip during a party run.

## User data dir and command-line args

- `OS.GetUserDataDir` is used only for logs and console helpers, not for saves.
- **No command-line argument changes the user data dir or the profile.** Seat isolation (M4) will need a patch, or Godot's own `--user-data-dir`-style override; compare with CouchCoop in 0.3.
- Args are parsed by `Core.Helpers.CommandLineHelper`:
  - `--clientId=N` (non-Steam local ID, which also changes `user://default/N/`);
  - `--force-steam on|off`;
  - `--nomods`;
  - `--fastmp` (use ENet instead of Steam).
- **Modded copy.** `ModManager.CopyUnmoddedSaveFilesIfNeeded` runs on the first modded launch, the one that produced the "modded profile reset" seen above.
- **Init order** (`NGame`): platform, then `ModManager.Initialize`, then `UserDataPathProvider.IsRunningModded`, then the profile ID.

## Combat turn flow and action sync

- **Turns.** Players act at the same time. Per-turn state lives in `Core.Combat.CombatManager` / `CombatTurnState`: `PlayersReadyToEndTurn`, `PlayersThatCannotUnEndTurn`, `PlayersReadyToBeginEnemyTurn`.
- **End Turn.**
  - `Core.Nodes.Combat.NEndTurnButton.OnRelease` enqueues `EndPlayerTurnAction` through `RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue`; when the turn is already ended it enqueues `UndoEndPlayerTurnAction` instead.
  - `NEndTurnButton.SecretEndTurnLogicViaFtue()` makes the same request without UI checks.
  - On a client the request goes to the host, which rebuilds the action with the **sender's** ID, so a client can only end its own turn. That fits the no-forged-actions rule.
  - `CombatManager.OnEndedTurnLocally()` should also be called to lock local input.
- **Waiting.**
  - `CombatManager.SetReadyToEndTurn` releases the turn when `AllPlayersReadyToEndTurn` is true, meaning the ready count equals `State.Players.Count`. Disconnected players are included in that count.
  - Dead players, and players not in the turn, are auto-readied.
  - There is a second all-players wait before the enemy turn.
  - **There is no timeout.**
- **Sync.**
  - `Core.GameActions.Multiplayer.ActionQueueSynchronizer` is host-authoritative: clients request, the host enqueues and broadcasts `ActionEnqueuedMessage`.
  - `CombatStateSynchronizer` exchanges player data and RNG before each combat.
- **Divergence.** `Core.Multiplayer.Game.ChecksumTracker` takes an XxHash32 of `NetFullCombatState` after every action and at phase changes. On a mismatch the host sends `StateDivergenceMessage` and disconnects the client.

## Enemy scaling ✔

- **HP.** `Core.Entities.Creatures.Creature.ScaleHpForMultiplayer` = base HP × players × `MultiplayerScalingModel.GetMultiplayerScaling(encounter, actIndex)`, with no change for one player.
  - The factor is 1.1 in act 1, 1.2 in act 2, 1.2 in act 3 (1.3 for the act-3 boss). Any other act index throws.
  - The formula is linear with no cap. At 10 players it gives 11× HP in act 1 and 13× for the act-3 boss.
- **Block.** `MultiplayerScalingModel.ModifyBlockMultiplicative`: × players for 2 or fewer; × players × factor for 3 or more.
- **Powers.** Those with `ShouldScaleInMultiplayer` scale by `PowerModel.GetScaledAmountForMultiplayer`: Artifact, Plating, Slippery, Regen, CurlUp and others; some override the formula.
- A few monsters call the HP formula directly: ToughEgg, TestSubject, DecimillipedeSegment, and the BattlewornDummy event.
- **Patch points for the multiplier:** `GetMultiplayerScaling` or `ScaleHpForMultiplayer`.

## Votes and party-wide choices

- **Map.** `Core.Multiplayer.Game.MapSelectionSynchronizer` holds one vote slot per player. It resolves on the host only when **every** slot has voted, then picks a random vote, so the odds follow vote counts.
- **Shared events.** `EventSynchronizer.PlayerVotedForSharedOptionIndex` works the same way.
- **Treasure.** `TreasureRoomRelicSynchronizer.OnPicked` waits for all players; contested relics go to rock-paper-scissors.
- **Act change.** `ActChangeSynchronizer.OnPlayerReady` waits for all players.
- **No timeouts and no partial resolution anywhere.** The only exception on disconnect is `RestSiteSynchronizer.OnPeerDisconnected`, which completes the dropped player's rest-site choice.

## Rewards

- `Core.Rooms.CombatRoom.OfferRoomEndRewards` builds one `RewardsSet` per player. Every peer generates all of them, but only the local player sees a screen.
- `Core.Multiplayer.Game.RewardsSetSynchronizer` handles choices: `SelectLocalReward`, `SkipLocalRewardsSet`, and `BeforeLeavingRoom`, which skips whatever is left.
- Rewards are **not** a party gate. Proceed (`RunManager.ProceedFromTerminalRewardsScreen`) opens the map, and the map vote is the gate.

## Layouts that assume at most 4 players

- **Campfire.** `NRestSiteRoom._ready` looks up `Character_1`..`Character_4`, so a 5th player is out of range. `NRestSiteCharacter` also has logic tied to specific indexes.
- **Treasure.** `NHandImage` rotates by `Index % 4`. `NTreasureRoomRelicCollection.InitializeRelics` uses only the authored layouts per relic count.
- **Already general.** Combat (`NCombatRoom.PositionPlayersAndPets`) and the shop (`NMerchantRoom.AfterRoomIsLoaded`) use a √N grid. The player panel list (`NMultiplayerPlayerStateContainer`) grows dynamically, but may run off the screen.

## Game modes

- `Core.Runs.GameMode` includes Blitz and Dread. `RunState.CreateShared` adds `BlitzRunModel` or `DreadRunModel`. `StartRunLobby` disconnects clients without `BlitzEpoch`/`DreadEpoch`.
- **Blitz.** `Core.Map.BlitzActMap` is one line: Ancient → Monster → Monster → Shop → Elite → Treasure → RestSite → Boss.
  - Between encounters, Proceed calls `RunManager.TryAdvanceToNextBlitzNode`, which casts the local map vote. The game moves on once everyone has pressed.
  - So there **is** a pause point after rewards. Patch point: `TryAdvanceToNextBlitzNode`.
- **Dread.** `DreadRunModel` adds Ascender's Bane to each player after act 1 and applies the boss's `DominionModel`.

## Unlocks in co-op

- Each `Player` carries its own `UnlockState`, taken from the lobby.
- `RunState.UnlockState` is the **union** of all players' states (`Core.Unlocks.UnlockState`).
- **Per player:** card rewards, transforms, shop cards and each player's own relic bag.
- **Union:** the shared treasure relic bag, act and room generation, Ancients and some events.
- **Characters:** the character picker uses the local player's own progress.
- **Ascension:** capped at the lowest maximum in the party (`StartRunLobby.UpdateMaxSharedAscension`).
- **Consequence for open question 10:** it is not "the host's unlocks". SpireParty's unlock setting must replace the `SerializableUnlockState` each client sends to the lobby.

## Disconnects

- **In the lobby.** `StartRunLobby.OnDisconnectedFromClientAsHost` frees the slot.
  - **Vanilla bug:** `LoadRunLobby.OnDisconnectedFromClientAsHost` never clears a client that dropped while still connecting, so the loaded lobby can wait forever.
- **Mid-run.** `RunLobby.OnDisconnectedFromClientAsHost` removes the peer from the connected set and raises `RemotePlayerDisconnected`. The `Player` stays in the run state, and the player can rejoin through `ClientRejoinRequestMessage`; the reply is `RunManager.GetRejoinMessage`.
- **Mid-combat.** No handling: `AllPlayersReadyToEndTurn` counts the disconnected player, so combat stalls until they rejoin.
- **Lost connection.** A client that loses the host goes back to the menu. `NMultiplayerTimeoutOverlay` is display only; real timeouts come from the transport (20 s).

---

# Second-process spike (M0 task 0.4, v0.113.1, Windows VM, 2026-11-16)

**Result: several game processes run side by side on one machine and one Steam account, and can play together over ENet on 127.0.0.1.** No patch is needed for any of this.

## What the game already supports

The game has developer switches for local multiplayer testing (`Core.Helpers.CommandLineHelper`; see `NGame.InitializePlatform`, `NMainMenu.CheckCommandLineArgs` and `NJoinFriendScreen.FastMpJoin`):

| Argument | Effect |
| --- | --- |
| `--force-steam off` | Skips Steamworks. The platform becomes `None`; saves go to `user://default/<localPlayerId>/` and the Steam profile is never touched |
| `--clientId=N` | Local player ID when Steam is off (default 1). Also the ENet net ID when joining (default 1000) |
| `--fastmp host_standard` (also `host`, `host_daily`, `host_blitz`, `host_dread`, `host_custom`) | Skips the menus and hosts over **ENet** on port 33771, even when Steam is on |
| `--fastmp join` | Skips the menus and joins `127.0.0.1:33771` over ENet |
| `--fastmp load` | Hosts the saved co-op run |
| `--log-file <path>` (Godot) | Per-process log file. Without it, every process writes to `user://logs/godot.log` |

## Tests run

| # | Host | Client | Result |
| --- | --- | --- | --- |
| 1 | `--force-steam off --nomods --fastmp host_standard` | `--force-steam off --nomods --fastmp join --clientId=1000` | Both in the character-select lobby ("Test Host", "Test Client 1"). Handshake OK: v0.113.1, hash 1748280997 |
| 2 | Same as 1, SpireParty loaded | Same as 1, SpireParty loaded | Both log the SpireParty startup line and `RUNNING MODDED`; handshake OK with matching mod lists; both in the lobby |
| 3 | Steam on, `SlayTheSpire2.exe` started directly | `--force-steam off` client | Host fails: "Steam failed to initialize … No appID found" |
| 4 | Steam on, started directly with env `SteamAppId=2868840` and `SteamGameId=2868840` | `--force-steam off` client | Steamworks initializes, the host uses the modded Steam profile, hosts over ENet; the client joins; both in the lobby |

Two idle processes in the lobby each used about 570 MB of RAM and under 10% of one core (10-core VM).

## Gotchas

- **Mods prompt per profile.** Each `user://default/<id>/` profile has its own `settings.save`, so mods start disabled and the game shows the mods prompt once. Accepting quits the game. The setting is `mod_settings.mods_enabled` in that profile's `settings.save` (nested; a top-level `mods_enabled` key is ignored). For seat profiles SpireParty can write this into the **seat's own** profile before launch. It never touches the Steam profile.
- **Shared folders.** All processes share `user://logs/`, `shader_cache/`, `sentry/` and the root user data dir. Only the profile tree is separated. Give each seat its own `--log-file`.
- **Direct launch with Steam on.** A Steam-enabled process started outside Steam needs `SteamAppId`/`SteamGameId` in its environment, or a `steam_appid.txt` in the game folder. Use the environment variables; don't add files to the game folder.
- **Window title.** A `--fastmp join` client sets its window title to "Slay The Spire 2 (Client)".

## Answers and consequences

- **Open question 6 ("Does a seat process need Steam?") — No.** The simplest assumption holds: a seat runs with `--force-steam off --clientId=<seat player id>`, and the game itself puts its saves and settings in `user://default/<id>/`. This also covers most of the "isolated seat profile" need without overriding Godot's user dir. Still to check in M4: the shared log, shader cache and Sentry folders cause no trouble with 9 seats.
- **The M1 Windows gate is feasible** on the VM alone: the host runs with Steam or without, and clients run `--force-steam off` with different `--clientId`. The Fedora checkpoint stays where the rearranged milestones put it.
- **Save surgery (0.4) can use this.** A 2-player co-op save can be made on the VM with `--force-steam off` processes (`--fastmp host_standard` plus `--fastmp join`), with no DirectConnectIP needed. Without Steam, the host's ID is 1 and the client's is 1000; the save lands in `user://default/1/profile1/saves/current_run_mp.save`.
- **Player IDs.** Over ENet, player IDs are whatever the client claims: the host is always 1. SpireParty's handshake can hand out seat player IDs and the client then joins with them; this supports open question 1's "one ID provider" assumption, to be confirmed in 0.3.

---

# Open questions answered (M0 task 0.3, v0.113.1)

"Holds" means the design's simplest assumption is right. "Partly" and "No" name the approach chosen instead. Details are in the area map above.

| # | Question | Holds? | Answer and chosen approach |
| --- | --- | --- | --- |
| 1 | Where does the client's player ID come from, and where is it validated? | **Partly** | Over ENet the client's ID is just the `netId` passed to `ENetClientConnectionInitializer(netId, ip, port)`, so SpireParty's Join menu passes the seat's player ID and needs no patch. The host's ID is hard-coded to 1 (`ENetHost.NetId`). Saves are matched to the host via `PlatformUtil.GetLocalPlayerId(platform)`, which returns the SteamID when Steam is on; `NMultiplayerSubmenu.StartLoad` / `StartHostAsync` pick the platform. Validation: `ENetHost.DoClientHandshake` (duplicates only), `LoadRunLobby` (`NotInSaveGame`), `RunManager.CanonicalizeSave` (host must be in the save). **Approach:** the host seat's player ID is fixed at 1 in every party; clients get IDs from the handshake; SpireParty loads party saves through `LoadAndCanonicalizeMultiplayerRunSave` with the party's host ID (DirectConnectIP patches this same method). |
| 2 | Which run-state objects reference a player ID? | **Partly** | Exactly four saved fields: `SerializablePlayer.NetId`, `SerializableRoom.ExtraRewards` keys (in `pre_finished_room`; `CombatRoom.FromSerializable` has no null check), `PlayerMapPointHistoryEntry.PlayerId` (each floor's history; `MapPointHistoryEntry.GetEntry` **throws** on a missing ID), and `SerializablePlayerMapDrawings.playerId` (tolerant). Order also matters: `Players` order is the slot index used by RNG helpers (`RunState.GetPlayerSlotIndex`, `Rng(player, id)`). Cards, relics, potions and powers hold no player IDs (`SavedProperties` cannot store a ulong). **Approach (fail closed, cleanup per reference):** *bench/remove* – remove the player, drop their `extra_rewards` key, keep their history and drawings; *add/restore* – append to the end of `Players` (never reorder existing players) and **backfill an empty history entry for the player on every past floor**, because the game-over screen (`NGameOverScreen`), progress and metrics code call `GetEntry` on every floor. Refuse any edit while `pre_finished_room` holds rewards for the affected player. |
| 3 | Can one player's state be saved and restored on its own? | **Yes** | `SerializablePlayer` is self-contained (deck, relics, potions, gold, HP, per-player RNG, odds, relic bag, unlock state) and rebuilt by `Player.FromSerializable`. Store it as `benched/<seat>.json` with the game's own serializer. Their per-floor history stays in the save. |
| 4 | Enemy health formula beyond 4 players? | **Yes** | `Creature.ScaleHpForMultiplayer`: base × players × act factor (1.1 / 1.2 / 1.2, 1.3 for the act-3 boss). Linear with no cap: 10 players means 11–13× HP. Block and some powers scale similarly. **Approach:** apply the party's enemy-health multiplier in a postfix on `ScaleHpForMultiplayer`. Chart the formula in M3 and decide whether the default multiplier should be below 1 at 10 players. |
| 5 | Code that assumes at most 4 players? | **No** (more than constants) | Literal 4s in all host entry points, plus **wire-format limits**: `StartRunLobbyPlayer.slotId` is 2 bits; player lists in `ClientLobbyJoinResponseMessage` and `LobbyBeginRunMessage` are 3 bits (7). UI: rest site (`Character_1..4`), treasure hands (`Index % 4`) and relic layouts, and the game-over rest-site branch. Combat, shop, votes, targeting, lobby lists and player panels are already dynamic but may overflow on screen. **Approach:** fallback as designed (vendor RMP's patches and fix screens one by one), **plus** patch the slot and list bit widths. Safe because every native player runs SpireParty, which the mod-list check enforces. |
| 6 | Does a seat process need Steam? | **Yes** (no Steam needed) | `--force-steam off --clientId=<id>` works and isolates saves in `user://default/<id>/` (second-process spike). **Caveat:** with Steam off, a seat cannot see Workshop-installed mods (`ModManager.ReadSteamMods` needs Steam). Install SpireParty into `mods/` on the host, or keep Steam on in seats as CouchCoop does, with cloud writes blocked. Decide in M4. |
| 7 | ENet over a 1280-byte tunnel? | **Unknown – test at the Fedora checkpoint** | The game sets no MTU, so Godot's built-in ENet default applies. It is larger than 1280, so full-size packets rely on the OS fragmenting UDP inside the tunnel. **The planned fallback "lower the MTU" is not possible:** Godot's C# API (`ENetConnection`, `ENetPacketPeer`) has no MTU setter. If the test fails, the fallback becomes a SpireParty transport: subclasses of the game's `NetHost`/`NetClient` that cap datagram size, as DirectConnectIP shows can be done. |
| 8 | Where are save paths resolved? | **Yes** | One helper: `UserDataPathProvider` (account, `modded`, profile dirs), plus per-file static helpers such as `RunSaveManager.GetRunSavePath`. Writes go through `CloudSaveStore` when Steam is on. **Approach:** redirect the co-op run save during party runs and keep party files out of the cloud store. |
| 9 | What is written when a run ends? | **Partly** (more than a handful) | `RunManager.OnEnded`: progress (`ProgressSaveManager.UpdateWithRunData` → `progress.save`), achievements (empty stub), run history `.run`, metrics upload, run-save delete, a Steam stat (`IncrementArchitectDamage`), daily score upload. Also `UpdateAfterCombatWon` mid-run, `NGameOverScreen` progress save, and main-menu abandon (`NMainMenu.AbandonRun`, `NMultiplayerSubmenu`). **Approach:** fallback as designed: patch each, redirect history to the party, prove with the folder-hash test. |
| 10 | Whose unlocks decide co-op card pools? | **No** | Not the host's. Each player's own `UnlockState` (sent in the lobby join request) drives their card rewards, shop cards and relic bag; `RunState.UnlockState` is the union of all players' unlocks for shared pools and generation; ascension is capped at the party's lowest. **Approach:** fallback as designed: SpireParty replaces the `SerializableUnlockState` each client sends, according to the party's unlock setting, so every client is identical. |
| 11 | Where can Blitz pause between encounters? | **Yes** | After rewards, Proceed calls `RunManager.TryAdvanceToNextBlitzNode`, which casts the map vote; the game moves on once all players have pressed. That is the safe point, and the save from after the combat already exists. |
| 12 | Can a client press its own End Turn from code? | **Yes** | Same call as the button: `ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(player, turnNumber))`, plus `CombatManager.OnEndedTurnLocally()`. The host rebuilds the action with the sender's ID, so a client can only end its own turn, which fits rule 6. |
| 13 | When does co-op autosave? | **Yes**, plus more | On room entry; also after every combat victory (rewards state, as `pre_finished_room`) and after Ancient events. Never mid-combat. Reload restarts an unfinished room, or returns to the rewards screen of a finished one. Only the host writes the co-op save. |
| 14 | What happens on a mid-combat disconnect? | **Yes**, with a better first option | Combat stalls: `AllPlayersReadyToEndTurn` counts the dropped player and has no timeout. **Over ENet the host is never told about a crashed client** (`ENetHost.Update` ignores the transport Disconnect event), so SpireParty's heartbeat must detect drops, or that event handling must be patched in. A dropped player can rejoin mid-combat through the vanilla rejoin flow (`ClientRejoinRequestMessage` → `RunManager.GetRejoinMessage`, carrying the combat state). The host can always use Save and Quit, which only returns to the menu; the room-entry save is kept. **Approach:** rejoin first; rewind-and-bench as the fallback. |
| 15 | Can a vote settle without a missing player? | **Yes** (it can't) | Map, shared-event, treasure and act-change votes all wait for every player, with no timeout. Only the rest site auto-completes for a disconnected peer. Nudge, auto-pass and kick cover it, as designed. |

## Other M0 research items

- **How the vanilla ENet transport is created, and how direct-connect mods reach it.** See Networking and transport above. DirectConnectIP does **not** reuse the game's `ENetHost`/`ENetClient`. It has its own `NetHost`/`NetClient` subclasses on Godot's `ENetConnection`, injected into `NetHostGameService`'s private `_netHost` by reflection, after replacing `NMultiplayerHostSubmenu.StartHostAsync` and `NMultiplayerSubmenu.StartHostAsync` with Harmony prefixes. Its custom ID patches `NullPlatformUtilStrategy.GetLocalPlayerId` and `SaveManager.LoadAndCanonicalizeMultiplayerRunSave`. Its offline takeover forges actions (the host plays offline players' cards), which rule 6 forbids for SpireParty v1. (Read only for study; no code copied.)
- **Manifest fields, `affects_gameplay` and the model-database hash at join.** See Mod loader and Lobby above. Gameplay-affecting mods are compared as exact `id-version` lists in `HandshakeManager.TryReadHandshakeMessage`; the model hash must match. SpireParty registers no content, so the hash stays vanilla.
- **Overriding Godot's user data dir per process.** No game argument does it. The options are (a) `--force-steam off --clientId=<id>` (the game's own per-ID profile under `user://default/<id>/`; shared logs and caches), or (b) CouchCoop's approach: point `APPDATA`/`LOCALAPPDATA`/`TEMP` (Windows) or `XDG_DATA_HOME` (Linux) at a per-seat folder, seed it from the host, and block Steam cloud writes in the seat. Option (a) is simpler; (b) is needed if seats keep Steam on for Workshop mods. Decide in M4.
- **`Image.SaveJpgToBuffer` availability.** Present in the game's GodotSharp (`Image.SaveJpgToBuffer(quality)`, also `SaveWebpToBuffer`). Speed is measured by the frame-read prototype (0.4).

## Design changes these answers imply

1. Lobby wire format must be widened (slot and player-list bit widths), not just the cap. Affects M3.
2. The host seat's player ID is always 1. Other seats get IDs from the host (the design's `7000003`-style IDs are fine for clients).
3. Roster "add/restore" must backfill per-floor history entries. Affects M2.
4. The MTU fallback is a custom transport, not an MTU setting. Affects M1 / the Fedora checkpoint.
5. The unlock handshake must replace each client's lobby unlock state. Affects M1/M2.
6. Drop detection needs a heartbeat or an `ENetHost` patch. The vanilla rejoin flow is the first answer to a mid-combat drop. Affects M1/M3.

---

# Save-surgery spike (M0 task 0.4, v0.113.1, Windows VM, 2026-11-16)

**Result: the bench, restore and add-with-backfill approach works with the game's own serializer, and the game loads and plays the edited saves.** The tool lives in `spikes/save-surgery/` (throwaway); it worked only on copies in `spikes/save-surgery/work/`.

## Setup

A 2-player save was made with `spikes/two-player.ps1`: host ID 1 and client ID 1000, both `--force-steam off`, with SpireParty loaded. One floor was played (Neow, then the first combat and its rewards), then Save and Quit. With mods loaded, the save is at `user://default/1/modded/profile1/saves/current_run_mp.save`.

## Steps and results

| Step | Result |
| --- | --- |
| Load and write with `JsonSerializationUtility.GetTypeInfo<SerializableRun>()` in a plain .NET 9 console app that references `sts2.dll` and `GodotSharp.dll` (no game running) | Works. The serializer needs no running engine; it only prints a harmless Sentry notice |
| Round trip without changes | **Not byte-identical**: 11 `"can_modify": true` lines appear. `SerializableMapPoint.CanBeModified` defaults to `true` but is written with `JsonIgnoreCondition.WhenWritingDefault`, so a stored `false` is omitted and reads back as `true`. **This is vanilla behavior**: the game's own load does the same, so the round trip matches what the game sees |
| Scan | Players `[1, 1000]`; `pre_finished_room` present with no `extra_rewards`; 1 history floor with both players; map drawings for both |
| Bench 1000 | Their `SerializablePlayer` went to a 5.5 KB JSON file and they were removed from `Players`. Their history entry and map drawings stay in the save |
| Restore 1000 | Appended to `Players`. **The output is byte-identical to the unchanged round trip**, so bench-then-restore loses nothing |
| Game loads the benched save (host only) | The load screen shows a 1-player run (HP, gold, act and floor correct), but **the run cannot start**. `LoadRunLobby.IsAboutToBeginGame` returns false in multiplayer whenever only one player is present. **Vanilla rule: a co-op save never resumes with one player** |
| Simulate a late joiner (remove 1000's history entries), then restore with backfill | Backfilled 1 empty `PlayerMapPointHistoryEntry` |
| Game loads the restored and backfilled save (host plus client 1000) | Both reach the load lobby; the run starts; both play a combat on the next floor. No exceptions, no state divergence (31 matching checksums on host and client). The new save has floor 2 history for both players |

## Consequences for M2

- **Use the game's serializer for roster edits**, as the design says. It works outside the game too, so roster-editing logic can be unit-tested on fixture saves without the game.
- **Minimum two active seats.** A party cannot resume a run with one active seat. **Decided 2026-11-17 (user): refuse** any bench or remove that would leave fewer than two active seats; no patch to `LoadRunLobby.IsAboutToBeginGame`. Recorded in `docs/DESIGN.md` → Join and leave mid-run.
- **Backfill is required and sufficient for loading.** Still unverified: the run-end paths that walk every floor (`NGameOverScreen` gold total, `ProgressSaveManager`, `MetricUtilities`, run-history screen) after a real mid-run add. Verify by finishing a run in M2.
- **Benched players' history stays in the save.** The game tolerated it here (load, play, save). `ScoreUtility` divides by the current player count, so the score may skew slightly; that is cosmetic.

---

# Frame-read spike (M0 task 0.4, v0.113.1, Windows VM with GPU passthrough, 2026-11-17)

**Result: reading the game's own viewport and JPEG-encoding it every frame at 1280x720 is cheap enough.** The game kept a steady 60 fps while capturing every frame.

## Method

A throwaway mod (`spikes/frame-read/`, armed only with `--frameread`, removed from `mods/` afterwards) hooked `RenderingServer.FramePostDraw`. No custom Godot node was needed; a C# event on an engine signal works from a mod.

- The window was set to 1280x720 windowed with `DisplayServer.WindowSetSize`.
- It recorded 10 s of baseline, then 30 s capturing every frame: `SceneTree.Root.GetTexture().GetImage()`, then `Image.SaveJpgToBuffer(0.70f)`.
- Process CPU was sampled from outside the game.
- The scene was a menu screen with light animation; busy combat scenes will give larger JPEGs.

## Results

| Measure | Baseline | Capturing every frame |
| --- | --- | --- |
| Frame rate | 60.0 fps (p95 frame 17.2 ms) | 60.0 fps (p95 17.1 ms, one 52 ms spike) |
| Process CPU | 22% of one core | 48% of one core |
| `GetImage` (viewport → CPU) | | mean 7.6 ms, p50 4.8, p95 12.3, max 23.8 |
| `SaveJpgToBuffer` q=0.70 | | mean 3.5 ms, p50 3.5, p95 3.7 |
| JPEG size | | mean 55 KB, max 66 KB (image is `Rgb8`, 1280x720) |

## Interpretation

- **CPU cost.** Capturing every frame added about 26% of one core, roughly 4.3 ms of CPU per frame. The rest of the measured `GetImage` time is waiting on the GPU, not CPU work.
- **At the design's seat-video default** (at most 15 fps, only when the frame changed), expect about **+6–7% of one core per seat**. For 9 seats that is well under one core of capture cost on top of each seat's own rendering. The open M4 question is GPU and RAM for 9 rendering seats (about 570 MB each), not the capture.
- **Bandwidth is the real limit for the JPEG MVP.**
  - At 55 KB × 15 fps ≈ 0.8 MB/s ≈ **6.6 Mbit/s per seat** while the picture changes, so about **60 Mbit/s for 9 seats**. That exceeds many home upload links.
  - Sending only changed frames helps a lot on quiet screens.
  - Otherwise lower the JPEG quality or frame rate, or move to the WebRTC step as the design already plans.
- **The capture stalls the main thread** (`GetImage` waits on the GPU, up to 24 ms worst case). If seats show hitches in M4:
  - encode on a worker thread;
  - use asynchronous GPU readback (Godot 4.4+ `RenderingDevice.TextureGetDataAsync`; check it exists in MegaDot's C# API before relying on it);
  - capture at most every 4th frame. A seat runs at 30 fps and captures at 15.

---

# M1 patch sites and findings (v0.113.1, d09d48e5)

## Patch sites

Every patch is in `src/SpireParty/GameAdapters/` and does nothing unless a SpireParty session is active.

| Adapter | Target | What it does |
| --- | --- | --- |
| MultiplayerMenu | `NMultiplayerSubmenu._Ready` (postfix) | Adds SpireParty Host and Join buttons: a fresh instance of the Host button's scene (`SceneFilePath`) with plain-text labels |
| RunSave | `RunSaveManager.SaveRun(SerializableRun, bool)`, `LoadMultiplayerRunSave`, `DeleteCurrentMultiplayerRun`, `HasMultiplayerRunSave` getter (prefixes) | During a host session the co-op save is `parties/<party>/run.save` (atomic write, `.backup` kept), never through `CloudSaveStore` |
| ProfileGuard | `SaveManager.UpdateProgressWithRunData` (returns `PostRunProgressUpdateResult.Unsuccessful`), `UpdateProgressAfterCombatWon`, `SaveProgressFile`, `SaveRunHistory` (redirected to `parties/<party>/history/`); `MetricUtilities.UploadRunMetrics(SerializableRun,bool,ulong)`, `UploadSettingsMetric`; `DailyRunUtility.UploadScore`; `StatsManager.IncrementArchitectDamage` | Skipped during a session, on host and clients. At session end, `SaveManager.InitProgressData()` reloads progress from disk, so in-memory changes from the party run are discarded |
| PeerBinding | `NetHostGameService.OnPeerConnected`, `OnPacketReceived` (prefixes) | An ENet peer's net ID and IP must match a fresh control-channel grant (read through `ENetHost._connectedPeers`), otherwise `DisconnectClient(InvalidJoin)`. Message header sender ID must equal the transport peer ID (header: 1-byte type, then a ulong sender, read with the game's `PacketReader`) |
| DevLaunch | `NMainMenu.CheckCommandLineArgs` (postfix) | `--spireparty-host=<party>` / `--spireparty-join=<addr>,<seat>[,<token>]` test switches |

## Findings

- **The game cannot rejoin a run in progress.** `JoinFlow.Begin` handles `RunSessionState.Running` by requesting a `ClientRejoinResponseMessage` (`RunManager.GetRejoinMessage`: `ToSave(null)` plus `NetFullCombatState.FromRun`), but nothing in the game consumes `JoinResult.rejoinResponse`: `NJoinFriendScreen` shows `RunInProgress` and disconnects. No code applies a `NetFullCombatState` on a client. Loading the save on the client would rebuild the current room and could diverge from the host. **M1 approach (the design's fallback):** a dropped player is reported to the host. The host chooses Save and Quit, reopens the party, and everyone rejoins the load lobby; the current room restarts. Live rejoin is deferred (needs its own spike).
- **Telling the game about a drop.** `NetHostGameService.DisconnectClient` → `ENetHost.DisconnectClient` calls the handler's `OnPeerDisconnected`, so the game treats it as a normal disconnect. This works around the ignored ENet `Disconnect` event without patching `ENetHost.Update`.
- **Disconnecting inside `OnPeerConnected` is safe.** `NetHostGameService.OnPeerDisconnected` tolerates a peer it never registered.
- **New profiles show the mods prompt** (each `default/<id>` test profile, once). Accepting it quits the game.
- **Names in the lobby.** Without Steam, the lobby shows the player ID (e.g. "7000002") for SpireParty seats. Seat display names in the lobby and in the run need a name patch (`PlatformUtil.GetPlayerName`); deferred.
- **The control channel's "auto" address** found this VM's Tailscale address (100.86.205.88) and bound it plus 127.0.0.1.

## M1 gate run 1: profile leak found and fixed (2026-11-17)

The first gate run's hash compare showed `progress.save` written **during** the party session, in the host's Steam modded profile and in the client's test profile, and `replays/latest.mcr` written to both profiles.

- **Cause 1: tutorial flags.** `ProgressSaveManager.MarkFtueAsComplete` ("Player has seen ftue …") calls `ProgressSaveManager.SaveProgress()` directly, not through `SaveManager.SaveProgressFile`. Five other callers do the same (`SetFtuesEnabled` and others). Each write saves the whole in-memory progress, so the mid-run write may also have carried small in-memory changes from the party run (the file grew by about 300 bytes). Fix: guard `ProgressSaveManager.SaveProgress()` itself, the single writer of `progress.save`.
- **Cause 2: combat replay.** `RunManager.WriteReplay` writes `replays/latest.mcr` into the profile after each combat. Fix: redirect it to `<party>/replays/latest.mcr`. Note: `CombatReplayWriter.WriteReplay` splits the path on '/'.
- **Impact.** Only the **modded** profile (`steam/<SteamID>/modded/profile1`) and test profiles changed. The vanilla profile was untouched. The changed modded `progress.save` cannot be restored, because its `.backup` was overwritten too.
- **Status.** The fix is deployed and self-checks pass. It must be confirmed with another hash compare over a session that triggers a progress write.
- **Re-check 1 (2026-11-17).** `progress.save` was clean: no write during the session, and the post-session write had the same hash. One `replays/latest.mcr` write still reached the profile **after** the session ended. `RunManager.CleanUp` writes a final replay after the net service disconnects, and clears `State` only afterwards. Fix: a session now ends only when the net service is disconnected **and** `RunManager.Instance.IsInProgress` is false.
