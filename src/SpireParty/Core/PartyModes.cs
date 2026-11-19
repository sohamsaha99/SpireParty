using System;
using System.Collections.Generic;
using System.Linq;

namespace SpireParty.Core;

/// <summary>
/// The game mode a party plays (M3 task 3.9, DESIGN → Saves, progression and game modes). Chosen when the
/// party is created (D4): one party = one run = one mode. Stored in party.json as <c>mode</c>.
/// Pure: the game's own <c>GameMode</c> enum is only used by the code that opens the lobby.
/// </summary>
public static class PartyModes
{
    public const string Standard = "standard";
    public const string Daily = "daily";
    public const string Custom = "custom";
    public const string Blitz = "blitz";
    public const string Dread = "dread";

    public static readonly string[] All = { Standard, Daily, Custom, Blitz, Dread };

    public static string Label(string mode) => mode switch
    {
        Daily => "Daily",
        Custom => "Custom",
        Blitz => "Blitz",
        Dread => "Dread",
        _ => "Standard",
    };

    /// <summary>One line for the mode picker.</summary>
    public static string Description(string mode) => mode switch
    {
        Daily => "Today's daily: fixed characters, seed and modifiers for the day the run starts",
        Custom => "Pick modifiers, seed and ascension in the lobby",
        Blitz => "A short run: one line of rooms per act",
        Dread => "The harder mode: a bane for everyone after each act",
        _ => "A normal run",
    };

    /// <summary>A known mode name (any case), or null.</summary>
    public static string? Parse(string? text)
    {
        string t = (text ?? "").Trim();
        return All.FirstOrDefault(m => string.Equals(m, t, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>What a custom-run modifier needs for a player added to a saved run.</summary>
/// <param name="Id">The modifier's type name, e.g. "DeadlyEvents".</param>
/// <param name="ClearsPlayerDeck">The modifier empties every deck at run creation (Draft, Insanity, Sealed Deck).</param>
/// <param name="ActsAtRunCreation">The modifier overrides <c>AfterRunCreated</c> (something happens only when the run is created).</param>
public sealed record ModifierTraits(string Id, bool ClearsPlayerDeck, bool ActsAtRunCreation);

/// <summary>
/// Whether a new player can be added to a custom run, and what must be done to them so they follow the
/// run's modifiers. <c>ModifierModel.OnRunCreated</c> runs only when a run is created, so a joiner misses it
/// (findings → M2, New player). Fail closed: a modifier that acts at run creation and is not known here refuses the add.
/// </summary>
public static class JoinerModifiers
{
    /// <summary>
    /// Modifiers whose run-creation step has a per-player part SpireParty repeats for a joiner.
    /// DeadlyEvents removes Juzu Bracelet from each player's relic bag (and the shared bag and the odds, which the
    /// save already holds).
    /// </summary>
    public static readonly IReadOnlySet<string> Repeatable = new HashSet<string>(StringComparer.Ordinal) { "DeadlyEvents" };

    /// <summary>Null if a player can be added; otherwise why not.</summary>
    public static string? Refusal(IEnumerable<ModifierTraits> modifiers)
    {
        foreach (var m in modifiers)
        {
            if (m.ClearsPlayerDeck)
                return $"This run uses the {m.Id} modifier, which replaces every starting deck at the start of the run. " +
                       "A player added now would keep a starter deck, so adding players is not possible in this run.";
            if (m.ActsAtRunCreation && !Repeatable.Contains(m.Id))
                return $"This run uses the {m.Id} modifier, which changes players when the run is created. " +
                       "SpireParty does not know how to apply it to a new player, so adding players is not possible in this run.";
        }
        return null;
    }
}
