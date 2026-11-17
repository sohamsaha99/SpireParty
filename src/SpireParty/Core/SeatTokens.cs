using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SpireParty.Core;

/// <summary>
/// Seat tokens like "amber-otter-41": easy to read aloud, about 2^18 combinations, so they
/// rely on the mesh VPN plus <see cref="FailedAttemptLimiter"/>. Stored only as salted
/// SHA-256 ("sha256$&lt;salt&gt;$&lt;hash&gt;", base64) and compared in constant time.
/// </summary>
public static class SeatTokens
{
    static readonly string[] Adjectives =
    {
        "amber", "azure", "bold", "brave", "brisk", "calm", "clever", "cosmic", "crimson", "curious",
        "dapper", "daring", "dusty", "eager", "early", "fancy", "fuzzy", "gentle", "giant", "golden",
        "grand", "happy", "hidden", "humble", "icy", "jolly", "keen", "kind", "lively", "lucky",
        "mellow", "merry", "mighty", "misty", "nimble", "noble", "olive", "plucky", "polite", "proud",
        "quick", "quiet", "rapid", "rosy", "rusty", "sandy", "shiny", "silent", "silver", "sleepy",
        "smooth", "snowy", "solar", "spicy", "steady", "sunny", "swift", "tidy", "tiny", "velvet",
        "vivid", "wandering", "witty", "zesty",
    };

    static readonly string[] Animals =
    {
        "badger", "bat", "bear", "beaver", "bison", "boar", "camel", "cat", "cobra", "crab",
        "crane", "crow", "deer", "dingo", "dolphin", "donkey", "duck", "eagle", "eel", "falcon",
        "ferret", "finch", "fox", "frog", "gecko", "goat", "goose", "hare", "hawk", "heron",
        "hippo", "ibis", "jackal", "koala", "lemur", "lion", "llama", "lynx", "mole", "moose",
        "newt", "otter", "owl", "panda", "parrot", "pelican", "puffin", "quail", "rabbit", "raven",
        "seal", "shark", "sloth", "snail", "squid", "stork", "swan", "tapir", "tiger", "toad",
        "turtle", "walrus", "wolf", "yak",
    };

    public static string Generate()
    {
        string adjective = Adjectives[RandomNumberGenerator.GetInt32(Adjectives.Length)];
        string animal = Animals[RandomNumberGenerator.GetInt32(Animals.Length)];
        int number = RandomNumberGenerator.GetInt32(10, 100);
        return $"{adjective}-{animal}-{number}";
    }

    /// <summary>Lower-case, trimmed, spaces and underscores treated as dashes.</summary>
    public static string Normalize(string? token) =>
        string.Join("-", (token ?? "").Trim().ToLowerInvariant()
            .Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries));

    public static string Hash(string token)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        return $"sha256${Convert.ToBase64String(salt)}${Convert.ToBase64String(Digest(salt, Normalize(token)))}";
    }

    public static bool Verify(string? token, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 3 || parts[0] != "sha256" || string.IsNullOrEmpty(token)) return false;
        try
        {
            byte[] salt = Convert.FromBase64String(parts[1]);
            byte[] expected = Convert.FromBase64String(parts[2]);
            return CryptographicOperations.FixedTimeEquals(Digest(salt, Normalize(token)), expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    static byte[] Digest(byte[] salt, string token) =>
        SHA256.HashData(salt.Concat(Encoding.UTF8.GetBytes(token)).ToArray());
}

/// <summary>
/// Blocks an address after too many failed claims within a sliding window. Pure: the clock is
/// injected so tests can control time.
/// </summary>
public sealed class FailedAttemptLimiter
{
    readonly int _maxFailures;
    readonly TimeSpan _window;
    readonly Func<DateTime> _now;
    readonly Dictionary<string, List<DateTime>> _failures = new();

    public FailedAttemptLimiter(int maxFailures = 5, TimeSpan? window = null, Func<DateTime>? now = null)
    {
        _maxFailures = maxFailures;
        _window = window ?? TimeSpan.FromMinutes(1);
        _now = now ?? (() => DateTime.UtcNow);
    }

    public bool IsBlocked(string address)
    {
        Prune(address);
        return _failures.TryGetValue(address, out var list) && list.Count >= _maxFailures;
    }

    public void RecordFailure(string address)
    {
        Prune(address);
        if (!_failures.TryGetValue(address, out var list)) _failures[address] = list = new List<DateTime>();
        list.Add(_now());
    }

    public void RecordSuccess(string address) => _failures.Remove(address);

    void Prune(string address)
    {
        if (!_failures.TryGetValue(address, out var list)) return;
        var cutoff = _now() - _window;
        list.RemoveAll(t => t <= cutoff);
        if (list.Count == 0) _failures.Remove(address);
    }
}
