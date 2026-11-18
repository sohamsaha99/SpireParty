using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpireParty.Core;

public enum SeatKind { Native, Browser }
public enum SeatState { Active, Benched, Empty }

public sealed class Seat
{
    public required string SeatId { get; init; }
    public required ulong PlayerId { get; init; }
    /// <summary>Salted hash from <see cref="SeatTokens.Hash"/>; empty = no token set yet.</summary>
    public string TokenHash { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public SeatKind Kind { get; set; } = SeatKind.Native;
    public SeatState State { get; set; } = SeatState.Active;
    /// <summary>Character model ID once the seat has joined the run; empty before.</summary>
    public string Character { get; set; } = "";
}

/// <summary>
/// One party = one run: <c>user://mods/SpireParty/parties/&lt;name&gt;/party.json</c>.
/// Defaults: <c>docs/DESIGN.md</c> → Defaults and configuration.
/// </summary>
public sealed class PartyFile
{
    public string Name { get; set; } = "";
    public int MaxPlayers { get; set; } = 10;
    /// <summary>"everything" or "mirrorHost".</summary>
    public string Unlocks { get; set; } = "everything";
    public double EnemyHealthMultiplier { get; set; } = 1.0;
    /// <summary>0 = off; otherwise 30, 45, 60 or 90 seconds.</summary>
    public int TurnTimerSeconds { get; set; }
    public int AfkStrikes { get; set; } = 2;
    /// <summary>"majority", "unanimous" or "hostOnly".</summary>
    public string VoteToKick { get; set; } = "majority";
    /// <summary>Gold: "highest" (default), "average", "lowest" or "starter". Max HP: "matchLowest" or "starterOnly".</summary>
    public string CatchUpGold { get; set; } = "highest";
    public string CatchUpMaxHp { get; set; } = "matchLowest";
    public int CatchUpCardRewards { get; set; }
    /// <summary>Next player ID to hand out; only ever increases, so IDs are never reused.</summary>
    public ulong NextPlayerId { get; set; } = SeatRules.FirstGuestPlayerId;
    public List<Seat> Seats { get; set; } = new();

    static readonly int[] TimerChoices = { 0, 30, 45, 60, 90 };

    public static PartyFile Parse(string? json, out List<string> warnings)
    {
        var j = ForgivingJson.Parse(json, "party.json");
        var p = new PartyFile
        {
            Name = j.String("name", ""),
            MaxPlayers = j.Int("maxPlayers", 10, 2, 10),
            Unlocks = j.Choice("unlocks", "everything", "everything", "mirrorHost"),
            EnemyHealthMultiplier = j.Double("enemyHealthMultiplier", 1.0, 0.5, 3.0),
            TurnTimerSeconds = j.Int("turnTimerSeconds", 0),
            AfkStrikes = j.Int("afkStrikes", 2, 1, 5),
            VoteToKick = j.Choice("voteToKick", "majority", "majority", "unanimous", "hostOnly"),
            NextPlayerId = j.ULong("nextPlayerId", SeatRules.FirstGuestPlayerId, SeatRules.FirstGuestPlayerId),
        };
        if (!TimerChoices.Contains(p.TurnTimerSeconds))
        {
            j.Warnings.Add($"party.json.turnTimerSeconds: expected one of 0, 30, 45, 60, 90; using 0 (off)");
            p.TurnTimerSeconds = 0;
        }
        var catchUp = j.Section("catchUp");
        // "matchLowest" and "starterOnly" are the M1 names for "lowest" and "starter".
        p.CatchUpGold = catchUp.Choice("gold", "highest", "highest", "average", "lowest", "starter", "matchLowest", "starterOnly") switch
        {
            "matchLowest" => "lowest",
            "starterOnly" => "starter",
            var g => g,
        };
        p.CatchUpMaxHp = catchUp.Choice("maxHp", "matchLowest", "matchLowest", "starterOnly");
        p.CatchUpCardRewards = catchUp.Int("cardRewards", 0, 0, 10);

        foreach (var s in j.Objects("seats"))
        {
            var seatId = SeatRules.Normalize(s.String("seatId", ""));
            ulong playerId = s.ULong("playerId", 0);
            if (!SeatRules.IsValidSeatId(seatId) || playerId == 0)
            {
                j.Warnings.Add($"party.json.seats: seat \"{seatId}\" has no valid seat ID or player ID; skipping it");
                continue;
            }
            if (p.Seats.Any(x => x.SeatId == seatId || x.PlayerId == playerId))
            {
                j.Warnings.Add($"party.json.seats: duplicate seat \"{seatId}\" or player ID {playerId}; skipping it");
                continue;
            }
            p.Seats.Add(new Seat
            {
                SeatId = seatId,
                PlayerId = playerId,
                TokenHash = s.String("tokenHash", ""),
                DisplayName = s.String("displayName", seatId),
                Kind = s.Choice("kind", "native", "native", "browser") == "browser" ? SeatKind.Browser : SeatKind.Native,
                State = s.Choice("state", "active", "active", "benched", "empty") switch
                {
                    "benched" => SeatState.Benched,
                    "empty" => SeatState.Empty,
                    _ => SeatState.Active,
                },
                Character = s.String("character", ""),
            });
        }

        // Never hand out an ID that a stored seat already uses, even if the file was hand-edited.
        ulong maxUsed = p.Seats.Where(s => s.PlayerId >= SeatRules.FirstGuestPlayerId)
                               .Select(s => s.PlayerId).DefaultIfEmpty(0UL).Max();
        if (maxUsed >= p.NextPlayerId) p.NextPlayerId = maxUsed + 1;

        warnings = j.Warnings;
        return p;
    }

    public string ToJson()
    {
        var o = new JsonObject
        {
            ["name"] = Name,
            ["maxPlayers"] = MaxPlayers,
            ["unlocks"] = Unlocks,
            ["enemyHealthMultiplier"] = EnemyHealthMultiplier,
            ["turnTimerSeconds"] = TurnTimerSeconds,
            ["afkStrikes"] = AfkStrikes,
            ["voteToKick"] = VoteToKick,
            ["catchUp"] = new JsonObject
            {
                ["gold"] = CatchUpGold,
                ["maxHp"] = CatchUpMaxHp,
                ["cardRewards"] = CatchUpCardRewards,
            },
            ["nextPlayerId"] = NextPlayerId,
            ["seats"] = new JsonArray(Seats.Select(s => (JsonNode)new JsonObject
            {
                ["seatId"] = s.SeatId,
                ["playerId"] = s.PlayerId,
                ["tokenHash"] = s.TokenHash,
                ["displayName"] = s.DisplayName,
                ["kind"] = s.Kind == SeatKind.Browser ? "browser" : "native",
                ["state"] = s.State.ToString().ToLowerInvariant(),
                ["character"] = s.Character,
            }).ToArray()),
        };
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
