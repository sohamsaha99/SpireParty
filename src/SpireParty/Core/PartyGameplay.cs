using System;

namespace SpireParty.Core;

/// <summary>
/// Party settings that change gameplay and must therefore be identical on every peer of a session:
/// the host takes them from its party.json, clients from the host's welcome, never from their own files.
/// Reset when the session ends.
/// </summary>
public static class PartyGameplay
{
    public const decimal MinEnemyHealth = 0.5m, MaxEnemyHealth = 3.0m;

    /// <summary>Enemy HP factor after the game's co-op scaling; 1 = the game's own values.</summary>
    public static decimal EnemyHealthMultiplier { get; private set; } = 1m;

    /// <summary>Turn timer in seconds, 0 = off (task 3.7).</summary>
    public static int TurnTimerSeconds { get; private set; }

    /// <summary>Timed-out turns in a row before a seat is marked AFK (task 3.8).</summary>
    public static int AfkStrikes { get; private set; } = 2;

    public static void Set(decimal enemyHealthMultiplier, int turnTimerSeconds = 0, int afkStrikes = 2)
    {
        EnemyHealthMultiplier = ClampEnemyHealth(enemyHealthMultiplier);
        TurnTimerSeconds = Array.IndexOf(PartySettings.TurnTimerChoices, turnTimerSeconds) >= 0 ? turnTimerSeconds : 0;
        AfkStrikes = Math.Clamp(afkStrikes, 1, 5);
    }

    public static void Reset() => Set(1m);

    /// <summary>Forgiving: anything outside 0.5–3.0 is clamped (as party.json's parser does).</summary>
    public static decimal ClampEnemyHealth(decimal value) => Math.Clamp(value, MinEnemyHealth, MaxEnemyHealth);

    /// <summary>
    /// An enemy's scaled HP times <paramref name="factor"/>, rounded up, at least 1. Applied only when
    /// more than one player is in the run (the game does not scale single-player HP either).
    /// </summary>
    public static decimal ScaleHp(decimal gameHp, decimal factor, int playerCount) =>
        playerCount <= 1 || factor == 1m ? gameHp : Math.Max(1m, Math.Ceiling(gameHp * factor));
}
