using System;
using MegaCrit.Sts2.Core.Logging;
using SpireParty.Core;

namespace SpireParty.Net;

/// <summary>
/// Party chat (user request 2026-11-19). Messages go over the SpireParty control channel, never the game's
/// own network messages, so they cannot affect game sync. A client sends <see cref="ChatSend"/>; the host
/// cleans it, rate-limits it, names the sender from the connection and relays a <see cref="ChatBroadcast"/>
/// to every client, the sender included. Lines live in memory for the session only; nothing is written
/// to disk, and the game log records only who sent how many characters.
/// All members run on the main thread.
/// </summary>
public static class PartyChat
{
    static readonly ChatRateLimiter Limiter = new();

    public static ChatLog History { get; } = new();

    /// <summary>A line arrived (main thread).</summary>
    public static event Action<ChatLine>? Received;

    /// <summary>Sends a message from the local player. Returns false when there is no session or nothing to send.</summary>
    public static bool Send(string? text)
    {
        string? clean = ChatRules.Clean(text);
        if (clean == null) return false;
        if (HostSession.Current is { } host)
        {
            Relay(host, SeatRules.HostPlayerId, PartyStore.HostSeatId, clean);
            return true;
        }
        return ClientSession.SendChat(clean);
    }

    /// <summary>Host: a client's message (main thread).</summary>
    internal static void OnClientSend(HostSession host, ControlSeat seat, ChatSend message)
    {
        string? clean = ChatRules.Clean(message.Text);
        if (clean == null) return;
        Relay(host, seat.PlayerId, seat.SeatId, clean);
    }

    static void Relay(HostSession host, ulong playerId, string seatId, string text)
    {
        if (!Limiter.Allow(playerId, DateTime.UtcNow))
        {
            Log.Info($"{ModEntry.LogTag} chat: dropped a message from seat \"{seatId}\" (too many at once)");
            return;
        }
        string name = ChatRules.Name(SeatNames.TryGet(playerId, out var n) ? n : null, seatId);
        host.Server.Broadcast(new ChatBroadcast(playerId, name, text));
        Deliver(new ChatLine(playerId, name, text));
    }

    /// <summary>Client: the host relayed a line (main thread).</summary>
    internal static void OnBroadcast(ChatBroadcast message)
    {
        string? text = ChatRules.Clean(message.Text);
        if (text == null) return;
        Deliver(new ChatLine(message.PlayerId, ChatRules.Name(message.Name, message.PlayerId.ToString()), text));
    }

    static void Deliver(ChatLine line)
    {
        History.Add(line);
        Log.Info($"{ModEntry.LogTag} chat: line from player {line.PlayerId} ({line.Text.Length} chars)");
        try { Received?.Invoke(line); }
        catch (Exception e) { Log.Error($"{ModEntry.LogTag} chat view failed: {e}"); }
    }

    /// <summary>The session ended: forget everything.</summary>
    public static void Clear()
    {
        History.Clear();
        Limiter.Clear();
    }
}
