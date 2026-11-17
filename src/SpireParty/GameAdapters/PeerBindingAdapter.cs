using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using SpireParty.Net;

namespace SpireParty.GameAdapters;

/// <summary>
/// Binds ENet peers to seats on a SpireParty host:
/// 1. NetHostGameService.OnPeerConnected: the peer's net ID must have just been granted by the
///    control channel to the same IP address, otherwise it is disconnected.
/// 2. NetHostGameService.OnPacketReceived: the sender ID written in the message header must match
///    the transport peer, so a client cannot speak for another player.
/// Only affects SpireParty's own host service; vanilla hosts are untouched.
/// </summary>
public sealed class PeerBindingAdapter : GameAdapter
{
    public override string Name => "PeerBinding";
    public override IReadOnlyList<string> Features => new[] { Features_.DirectConnect };

    static readonly FieldInfo? ConnectedPeersField = AccessTools.Field(typeof(ENetHost), "_connectedPeers");
    static readonly FieldInfo? HandshakeManagerField = AccessTools.Field(typeof(NetHostGameService), "_handshakeManager");
    static MethodInfo? _isHandshaking;

    public override string? SelfCheck()
    {
        var t = typeof(NetHostGameService);
        string? missing = Require(t, nameof(NetHostGameService), nameof(NetHostGameService.OnPeerConnected), new[] { typeof(ulong) })
            ?? Require(t, nameof(NetHostGameService), nameof(NetHostGameService.OnPacketReceived),
                new[] { typeof(ulong), typeof(byte[]), typeof(NetTransferMode), typeof(int) });
        if (missing != null) return missing;
        if (ConnectedPeersField == null) return "field ENetHost._connectedPeers not found";
        if (HandshakeManagerField == null) return "field NetHostGameService._handshakeManager not found";
        _isHandshaking = AccessTools.Method(HandshakeManagerField.FieldType, "IsHandshaking", new[] { typeof(ulong) });
        if (_isHandshaking == null) return "method HandshakeManager.IsHandshaking not found";
        var elementType = ConnectedPeersField.FieldType.GetGenericArguments();
        if (elementType.Length != 1 || AccessTools.Field(elementType[0], "peer") == null || AccessTools.Field(elementType[0], "netId") == null)
            return "ENetHost connection record no longer has peer/netId";
        return null;
    }

    public override void Apply(Harmony harmony)
    {
        var t = typeof(NetHostGameService);
        harmony.Patch(AccessTools.Method(t, nameof(NetHostGameService.OnPeerConnected), new[] { typeof(ulong) }),
            prefix: new HarmonyMethod(typeof(PeerBindingAdapter), nameof(OnPeerConnectedPrefix)));
        harmony.Patch(AccessTools.Method(t, nameof(NetHostGameService.OnPacketReceived),
                new[] { typeof(ulong), typeof(byte[]), typeof(NetTransferMode), typeof(int) }),
            prefix: new HarmonyMethod(typeof(PeerBindingAdapter), nameof(OnPacketReceivedPrefix)));
    }

    static HostSession? SessionFor(NetHostGameService service) =>
        HostSession.Current is { } s && ReferenceEquals(s.NetService, service) ? s : null;

    static bool OnPeerConnectedPrefix(NetHostGameService __instance, ulong peerId)
    {
        if (SessionFor(__instance) is not { } session) return true;
        IPAddress? address = PeerAddress(__instance.NetHost as ENetHost, peerId);
        if (address != null && session.Server.HasGrant(address, peerId)) return true;

        Log.Warn($"{ModEntry.LogTag} refused game connection for player {peerId} from {address?.ToString() ?? "unknown address"}: " +
                 "no matching SpireParty seat claim");
        __instance.DisconnectClient(peerId, NetError.InvalidJoin);
        return false;
    }

    static readonly PacketReader Reader = new();

    static bool OnPacketReceivedPrefix(NetHostGameService __instance, ulong senderId, byte[] packetBytes)
    {
        if (SessionFor(__instance) == null) return true;
        try
        {
            object? handshakes = HandshakeManagerField!.GetValue(__instance);
            if (handshakes != null && (bool)_isHandshaking!.Invoke(handshakes, new object[] { senderId })!) return true;
            if (packetBytes.Length < 9) return true;   // let the game reject malformed packets itself
            Reader.Reset(packetBytes);
            Reader.ReadByte();
            ulong claimed = Reader.ReadULong();
            if (claimed == senderId) return true;
            Log.Warn($"{ModEntry.LogTag} dropped a message from player {senderId} that claimed to be from player {claimed}");
            return false;
        }
        catch (Exception e)
        {
            Log.Warn($"{ModEntry.LogTag} sender check failed ({e.Message}); dropping the message");
            return false;
        }
    }

    static IPAddress? PeerAddress(ENetHost? host, ulong peerId)
    {
        if (host == null || ConnectedPeersField?.GetValue(host) is not IEnumerable peers) return null;
        foreach (object connection in peers)
        {
            var type = connection.GetType();
            if ((ulong)AccessTools.Field(type, "netId").GetValue(connection)! != peerId) continue;
            if (AccessTools.Field(type, "peer").GetValue(connection) is ENetPacketPeer peer
                && IPAddress.TryParse(peer.GetRemoteAddress(), out var ip))
                return ip;
        }
        return null;
    }
}
