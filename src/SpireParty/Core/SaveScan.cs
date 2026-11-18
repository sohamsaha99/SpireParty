using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpireParty.Core;

/// <summary>
/// Fail-closed scan (DESIGN → Join and leave, hard rule 10): finds every place a player ID appears in a
/// run save's JSON, as a number, as a string, or as an object key, so a roster edit can refuse when the
/// ID sits somewhere it does not know how to clean up. Catches fields a future game patch adds.
/// Paths look like <c>$.players[1].net_id</c>; an object key is written <c>$.a.b{key}</c>.
/// </summary>
public static class SaveScan
{
    /// <summary>
    /// The places roster edits handle (findings → Player references in the run state, v0.113.1):
    /// the player's own entry; their reward key in the last room (dropped on leave, refused if not empty);
    /// their per-floor history (kept on leave, backfilled on join).
    /// <c>map_drawings</c> also holds player IDs, but inside a compressed blob this scan cannot read;
    /// the game tolerates drawings of absent players (save-surgery spike), so it is left as is.
    /// </summary>
    static readonly Regex[] Known =
    {
        new(@"^\$\.players\[\d+\]\.net_id$"),
        new(@"^\$\.pre_finished_room\.extra_rewards\{\d+\}$"),
        new(@"^\$\.map_point_history\[\d+\]\[\d+\]\.player_stats\[\d+\]\.player_id$"),
    };

    public static List<string> FindReferences(byte[] saveJson, ulong playerId)
    {
        using var doc = JsonDocument.Parse(saveJson);
        var found = new List<string>();
        Walk(doc.RootElement, "$", playerId.ToString(CultureInfo.InvariantCulture), found);
        return found;
    }

    /// <summary>References outside the known places; empty means the edit is safe to make.</summary>
    public static List<string> FindUnknownReferences(byte[] saveJson, ulong playerId) =>
        FindReferences(saveJson, playerId).Where(p => !Known.Any(k => k.IsMatch(p))).ToList();

    static void Walk(JsonElement e, string path, string id, List<string> found)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in e.EnumerateObject())
                {
                    if (prop.Name == id) found.Add($"{path}{{{prop.Name}}}");
                    Walk(prop.Value, $"{path}.{prop.Name}", id, found);
                }
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in e.EnumerateArray()) Walk(item, $"{path}[{i++}]", id, found);
                break;
            case JsonValueKind.Number:
                if (e.GetRawText() == id) found.Add(path);
                break;
            case JsonValueKind.String:
                if (e.GetString() == id) found.Add(path);
                break;
        }
    }
}
