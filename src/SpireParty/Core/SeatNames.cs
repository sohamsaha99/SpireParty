using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SpireParty.Core;

/// <summary>
/// Player ID → display name for the running session: from party.json on the host, from the welcome and
/// roster messages on clients. Read by <c>NamesAdapter</c>, which shows these names instead of player IDs.
/// Written from the control channel's threads, read on the main thread: the dictionary is replaced whole.
/// </summary>
public static class SeatNames
{
    public const int MaxLength = 24;

    static volatile IReadOnlyDictionary<ulong, string> _names = new Dictionary<ulong, string>();

    public static void Set(IReadOnlyDictionary<ulong, string> names) => _names = new Dictionary<ulong, string>(names);
    public static void Clear() => _names = new Dictionary<ulong, string>();

    public static bool TryGet(ulong playerId, out string name)
    {
        if (_names.TryGetValue(playerId, out var n) && n.Length > 0)
        {
            name = n;
            return true;
        }
        name = "";
        return false;
    }

    /// <summary>The names of every seat that is not removed (display name, or the seat ID if it has none).</summary>
    public static Dictionary<ulong, string> Of(PartyFile party) =>
        party.Seats.Where(s => s.State != SeatState.Empty)
                   .ToDictionary(s => s.PlayerId, s => Clean(s.DisplayName) is { Length: > 0 } n ? n : s.SeatId);

    /// <summary>Trimmed, inner whitespace collapsed, control and BBCode bracket characters removed, at most <see cref="MaxLength"/> characters.</summary>
    public static string Clean(string? name)
    {
        var sb = new StringBuilder();
        bool space = false;
        foreach (char c in (name ?? "").Trim())
        {
            if (char.IsWhiteSpace(c)) { space = true; continue; }
            if (char.IsControl(c) || c is '[' or ']' or '​' or '‎' or '‏') continue;
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        string s = sb.ToString();
        if (s.Length > MaxLength) s = s[..MaxLength].TrimEnd();
        if (s.Length > 0 && char.IsHighSurrogate(s[^1])) s = s[..^1];
        return s;
    }
}
