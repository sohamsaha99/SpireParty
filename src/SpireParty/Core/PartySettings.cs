using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SpireParty.Core;

/// <summary>
/// The party settings the host can change from the Settings screen (DESIGN → Defaults and configuration),
/// with the choices offered for each. Pure: <see cref="PartyEditor.SaveSettings"/> backs up and writes.
/// Changes take effect at the next Continue (a running session keeps the values it started with).
/// </summary>
public sealed record PartySettings(int MaxPlayers, double EnemyHealthMultiplier, int TurnTimerSeconds, int AfkStrikes)
{
    public static readonly int[] MaxPlayerChoices = Enumerable.Range(RosterEdit.MinPlayers, WireWidths.MaxPlayers - RosterEdit.MinPlayers + 1).ToArray();
    public static readonly double[] EnemyHealthChoices = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 2.5, 3.0 };
    public static readonly int[] TurnTimerChoices = { 0, 30, 45, 60, 90 };
    public static readonly int[] AfkStrikeChoices = { 1, 2, 3, 4, 5 };

    public static PartySettings Of(PartyFile party) =>
        new(party.MaxPlayers, party.EnemyHealthMultiplier, party.TurnTimerSeconds, party.AfkStrikes);

    /// <summary>Null if these settings can be saved for this party and its saved run, otherwise why not.</summary>
    public string? Check(int playersInRun, int gameCap)
    {
        if (!MaxPlayerChoices.Contains(MaxPlayers)) return $"Max players must be {MaxPlayerChoices[0]} to {MaxPlayerChoices[^1]}.";
        if (MaxPlayers > gameCap) return $"This game can carry at most {gameCap} players (see the startup message).";
        if (playersInRun > MaxPlayers)
            return $"The saved run already has {playersInRun} players; bench or remove some first, or keep max players at {playersInRun} or more.";
        if (EnemyHealthMultiplier < (double)PartyGameplay.MinEnemyHealth || EnemyHealthMultiplier > (double)PartyGameplay.MaxEnemyHealth)
            return "Enemy health must be 0.5 to 3.0.";
        if (!TurnTimerChoices.Contains(TurnTimerSeconds)) return "Turn timer must be off, 30, 45, 60 or 90 seconds.";
        if (!AfkStrikeChoices.Contains(AfkStrikes)) return "AFK strikes must be 1 to 5.";
        return null;
    }

    public void ApplyTo(PartyFile party)
    {
        party.MaxPlayers = MaxPlayers;
        party.EnemyHealthMultiplier = EnemyHealthMultiplier;
        party.TurnTimerSeconds = TurnTimerSeconds;
        party.AfkStrikes = AfkStrikes;
    }

    /// <summary>What changed, for the backup's reason and the status line ("" when nothing did).</summary>
    public string Diff(PartySettings before)
    {
        var parts = new List<string>();
        if (MaxPlayers != before.MaxPlayers) parts.Add($"max players {MaxPlayers}");
        if (EnemyHealthMultiplier != before.EnemyHealthMultiplier) parts.Add($"enemy health {Health(EnemyHealthMultiplier)}");
        if (TurnTimerSeconds != before.TurnTimerSeconds) parts.Add($"turn timer {Timer(TurnTimerSeconds)}");
        if (AfkStrikes != before.AfkStrikes) parts.Add($"AFK after {AfkStrikes}");
        return string.Join(", ", parts);
    }

    public static string Health(double x) => "×" + x.ToString("0.##", CultureInfo.InvariantCulture);
    public static string Timer(int seconds) => seconds == 0 ? "off" : $"{seconds} s";

    /// <summary>One line for the party screen.</summary>
    public string Summary() =>
        $"Up to {MaxPlayers} players · enemy health {Health(EnemyHealthMultiplier)} · turn timer {Timer(TurnTimerSeconds)} · AFK after {AfkStrikes} timed-out turn{(AfkStrikes == 1 ? "" : "s")}";
}
