using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SpireParty.Net;

/// <summary>Messages on the SpireParty control channel (TCP, game port number).</summary>
public abstract record ControlMessage;

/// <summary>Client → host, first message. Token is empty when asking for a new seat. Name is the display name the player typed (empty = keep).</summary>
public sealed record Hello(string SpirePartyVersion, string GameVersion, string GameCommit, string SeatId, string Token, string Name = "") : ControlMessage;

/// <summary>
/// Host → client. <see cref="NewToken"/> is set only when the seat was just created or claimed while open.
/// <see cref="HostUnlocks"/> (the game's JSON for the host's unlock state) and the host's ascension limits are set
/// only when <see cref="Unlocks"/> is "mirrorHost". <see cref="EnemyHealthMultiplier"/> is the party's setting
/// (missing = 1, the game's own values); every peer must use the host's, since every peer computes enemy HP.
/// </summary>
public sealed record Welcome(ulong PlayerId, string SeatId, string PartyName, string? NewToken, string Unlocks, int HeartbeatSeconds,
                             IReadOnlyDictionary<ulong, string>? Names = null,
                             string? HostUnlocks = null, int HostMaxStandardAscension = 0, int HostMaxBlitzAscension = 0,
                             decimal EnemyHealthMultiplier = 1m) : ControlMessage;

/// <summary>Host → clients whenever a seat's display name changes: every seat's name, by player ID.</summary>
public sealed record Roster(IReadOnlyDictionary<ulong, string> Names) : ControlMessage;

/// <summary>Host → client, then the host closes the connection.</summary>
public sealed record Refuse(string Code, string Reason) : ControlMessage;

public sealed record Heartbeat : ControlMessage;

/// <summary>Either side, before closing on purpose (so the other side does not treat it as a drop).</summary>
public sealed record Bye(string Reason) : ControlMessage;

/// <summary>
/// Framing: 4-byte big-endian length, then UTF-8 JSON with a "type" field.
/// Hand-written JSON (no reflection-based serializer) so it works in any runtime configuration.
/// </summary>
public static class ControlCodec
{
    /// <summary>A welcome in mirrorHost mode carries the host's unlock state (a few KB); this leaves ample room.</summary>
    public const int MaxFrameBytes = 64 * 1024;

    public static byte[] Encode(ControlMessage message)
    {
        JsonObject o = message switch
        {
            Hello h => new JsonObject
            {
                ["type"] = "hello", ["spireparty"] = h.SpirePartyVersion, ["gameVersion"] = h.GameVersion,
                ["gameCommit"] = h.GameCommit, ["seatId"] = h.SeatId, ["token"] = h.Token, ["name"] = h.Name,
            },
            Welcome w => new JsonObject
            {
                ["type"] = "welcome", ["playerId"] = w.PlayerId, ["seatId"] = w.SeatId, ["party"] = w.PartyName,
                ["newToken"] = w.NewToken, ["unlocks"] = w.Unlocks, ["heartbeatSeconds"] = w.HeartbeatSeconds,
                ["names"] = w.Names == null ? null : EncodeNames(w.Names),
                ["hostUnlocks"] = w.HostUnlocks, ["hostMaxStandardAscension"] = w.HostMaxStandardAscension,
                ["hostMaxBlitzAscension"] = w.HostMaxBlitzAscension, ["enemyHealthMultiplier"] = w.EnemyHealthMultiplier,
            },
            Roster r => new JsonObject { ["type"] = "roster", ["names"] = EncodeNames(r.Names) },
            Refuse r => new JsonObject { ["type"] = "refuse", ["code"] = r.Code, ["reason"] = r.Reason },
            Heartbeat => new JsonObject { ["type"] = "heartbeat" },
            Bye b => new JsonObject { ["type"] = "bye", ["reason"] = b.Reason },
            _ => throw new ArgumentException($"unknown message {message.GetType().Name}"),
        };
        byte[] json = Encoding.UTF8.GetBytes(o.ToJsonString());
        if (json.Length > MaxFrameBytes) throw new InvalidDataException("control message too large");
        var frame = new byte[4 + json.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, json.Length);
        json.CopyTo(frame, 4);
        return frame;
    }

    /// <summary>Parses one JSON payload. Throws <see cref="InvalidDataException"/> on anything malformed.</summary>
    public static ControlMessage Decode(ReadOnlySpan<byte> json)
    {
        JsonObject o;
        try
        {
            o = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("not a JSON object");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"bad JSON: {e.Message}");
        }

        string Str(string key) => o[key] is JsonValue v && v.TryGetValue(out string? s) ? s : "";
        try
        {
            return Str("type") switch
            {
                "hello" => new Hello(Str("spireparty"), Str("gameVersion"), Str("gameCommit"), Str("seatId"), Str("token"), Str("name")),
                "welcome" => new Welcome(o["playerId"]!.GetValue<ulong>(), Str("seatId"), Str("party"),
                    o["newToken"] is JsonValue t && t.TryGetValue(out string? nt) ? nt : null,
                    Str("unlocks"), o["heartbeatSeconds"]?.GetValue<int>() ?? 2, DecodeNames(o["names"]),
                    o["hostUnlocks"] is JsonValue hu && hu.TryGetValue(out string? hus) ? hus : null,
                    o["hostMaxStandardAscension"]?.GetValue<int>() ?? 0, o["hostMaxBlitzAscension"]?.GetValue<int>() ?? 0,
                    o["enemyHealthMultiplier"] is JsonValue m && m.TryGetValue(out decimal mul) ? mul : 1m),
                "roster" => new Roster(DecodeNames(o["names"]) ?? new Dictionary<ulong, string>()),
                "refuse" => new Refuse(Str("code"), Str("reason")),
                "heartbeat" => new Heartbeat(),
                "bye" => new Bye(Str("reason")),
                var other => throw new InvalidDataException($"unknown message type \"{other}\""),
            };
        }
        catch (Exception e) when (e is not InvalidDataException)
        {
            throw new InvalidDataException($"bad message: {e.Message}");
        }
    }

    static JsonObject EncodeNames(IReadOnlyDictionary<ulong, string> names)
    {
        var o = new JsonObject();
        foreach (var (id, name) in names.OrderBy(n => n.Key)) o[id.ToString(CultureInfo.InvariantCulture)] = name;
        return o;
    }

    /// <summary>Null when the field is missing; entries with a bad key or a non-text value are skipped.</summary>
    static IReadOnlyDictionary<ulong, string>? DecodeNames(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        var names = new Dictionary<ulong, string>();
        foreach (var (key, value) in o)
            if (ulong.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id)
                && value is JsonValue v && v.TryGetValue(out string? name))
                names[id] = name;
        return names;
    }

    public static async Task WriteAsync(Stream stream, ControlMessage message, CancellationToken ct)
    {
        byte[] frame = Encode(message);
        await stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads one frame. Returns null on a clean end of stream.</summary>
    public static async Task<ControlMessage?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactlyOrEnd(stream, header, ct).ConfigureAwait(false)) return null;
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > MaxFrameBytes) throw new InvalidDataException($"bad frame length {length}");
        var payload = new byte[length];
        if (!await ReadExactlyOrEnd(stream, payload, ct).ConfigureAwait(false))
            throw new EndOfStreamException("connection closed mid-message");
        return Decode(payload);
    }

    static async Task<bool> ReadExactlyOrEnd(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0) return false;
                throw new EndOfStreamException("connection closed mid-message");
            }
            read += n;
        }
        return true;
    }
}
