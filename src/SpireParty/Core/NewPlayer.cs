using System;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SpireParty.Core;

/// <summary>
/// Builds a brand-new player for a saved run with the game's own code, the way a run start does:
/// starter deck and relics (<c>Player.CreateForNewRun</c>), per-player RNG seeded from the run seed
/// and the new slot (<c>RunState.AddPlayerDebug</c>), relic grab bag, ascension effects.
/// Needs the game's model database, so it runs only inside the game. Findings: M2 → New player.
/// </summary>
public static class NewPlayer
{
    public static CharacterModel? FindCharacter(string name)
    {
        string n = name.Trim();
        return ModelDb.AllCharacters.FirstOrDefault(c =>
            string.Equals(c.Id.Entry, n, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.Id.ToString(), n, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the new player's serialized state. <paramref name="saveJson"/> is read into a scratch
    /// <see cref="RunState"/> only; the caller's own copy of the save is never touched here.
    /// </summary>
    public static SerializablePlayer Build(string saveJson, CharacterModel character, UnlockState unlocks, ulong netId, int extraBanes = 0)
    {
        var scratch = JsonSerializationUtility.FromJson<SerializableRun>(saveJson).SaveData
                      ?? throw new InvalidOperationException("The run save could not be read.");
        if (scratch.Players.Any(p => p.NetId == netId))
            throw new InvalidOperationException($"Player {netId} is already in the run.");

        var state = RunState.FromSerializable(scratch);
        var player = Player.CreateForNewRun(character, unlocks, netId);
        // Appends (slot = current player count), sets RunState, seeds PlayerRng from the run seed and slot,
        // runs AfterCreated on the starter cards and registers them with the run.
        state.AddPlayerDebug(player, -1);

        // RunManager.InitializeNewRun does these for players at a run start. AddPlayerDebug does them only
        // while a run is in progress, so do them here. The relic bag gets its own deterministic RNG so the
        // run's shared RNG streams in the save stay untouched.
        ulong seed = StringHelper.GetDeterministicHashCode(scratch.SerializableRng.Seed ?? "") ^ netId;
        player.PopulateRelicGrabBagIfNecessary(new Rng(seed, "spireparty_new_player"));
        new AscensionManager(state.GameMode, state.AscensionLevel).ApplyEffectsTo(player);

        // Dread: the banes the party collected at each act change (CatchUp.DreadBanes), made the way
        // AscensionManager adds its own bane.
        for (int i = 0; i < extraBanes; i++)
        {
            var bane = state.CreateCard<AscendersBane>(player);
            bane.FloorAddedToDeck = 1;
            player.Deck.AddInternal(bane, -1, silent: true);
        }

        return player.ToSerializable();
    }
}
