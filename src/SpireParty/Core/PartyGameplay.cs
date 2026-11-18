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

    public static void Set(decimal enemyHealthMultiplier) => EnemyHealthMultiplier = ClampEnemyHealth(enemyHealthMultiplier);

    public static void Reset() => EnemyHealthMultiplier = 1m;

    /// <summary>Forgiving: anything outside 0.5–3.0 is clamped (as party.json's parser does).</summary>
    public static decimal ClampEnemyHealth(decimal value) => Math.Clamp(value, MinEnemyHealth, MaxEnemyHealth);

    /// <summary>
    /// An enemy's scaled HP times <paramref name="factor"/>, rounded up, at least 1. Applied only when
    /// more than one player is in the run (the game does not scale single-player HP either).
    /// </summary>
    public static decimal ScaleHp(decimal gameHp, decimal factor, int playerCount) =>
        playerCount <= 1 || factor == 1m ? gameHp : Math.Max(1m, Math.Ceiling(gameHp * factor));
}
