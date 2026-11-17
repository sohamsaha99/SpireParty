using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SpireParty.Net;

/// <summary>Where the control channel listens, and which source addresses it accepts.</summary>
public sealed record ListenPlan(IReadOnlyList<IPAddress> BindAddresses, Func<IPAddress, bool> IsAllowedSource, string Description);

public static class ListenAddresses
{
    /// <summary>Tailscale and NordVPN Meshnet both hand out addresses in 100.64.0.0/10 (CGNAT range).</summary>
    public static bool IsMeshAddress(IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (a.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = a.GetAddressBytes();
        return b[0] == 100 && (b[1] & 0xC0) == 64;
    }

    public static bool IsLoopback(IPAddress a) => IPAddress.IsLoopback(a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a);

    /// <summary>
    /// "auto": every local mesh address plus 127.0.0.1 (browser seats and local tests connect
    /// over loopback); only mesh and loopback sources are accepted.
    /// "any": all interfaces, any source (for networks other than a mesh VPN; documented as less safe).
    /// Otherwise an explicit local IP address, plus loopback.
    /// </summary>
    public static ListenPlan Resolve(string setting, IEnumerable<IPAddress>? localAddresses = null)
    {
        string s = (setting ?? "").Trim().ToLowerInvariant();
        if (s == "any")
            return new ListenPlan(new[] { IPAddress.Any }, _ => true, "all interfaces (any source)");

        if (s != "auto" && s.Length > 0 && IPAddress.TryParse(s, out var explicitAddress))
            return new ListenPlan(Distinct(explicitAddress, IPAddress.Loopback),
                a => IsLoopback(a) || IsMeshAddress(a) || Same(a, explicitAddress, prefixOnly: true),
                $"{explicitAddress} and 127.0.0.1");

        var mesh = (localAddresses ?? LocalIPv4()).Where(IsMeshAddress).ToList();
        string description = mesh.Count == 0
            ? "127.0.0.1 only (no Tailscale or Meshnet address found)"
            : $"{string.Join(", ", mesh)} and 127.0.0.1";
        return new ListenPlan(Distinct(mesh.Append(IPAddress.Loopback).ToArray()),
            a => IsLoopback(a) || IsMeshAddress(a), description);
    }

    static IReadOnlyList<IPAddress> Distinct(params IPAddress[] addresses) => addresses.Distinct().ToList();

    static bool Same(IPAddress a, IPAddress b, bool prefixOnly)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (!prefixOnly) return a.Equals(b);
        // Same /24 as an explicit LAN address: allows a plain LAN setup when the user asks for it.
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        return x.Length == 4 && y.Length == 4 && x[0] == y[0] && x[1] == y[1] && x[2] == y[2];
    }

    static IEnumerable<IPAddress> LocalIPv4()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(u => u.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return Array.Empty<IPAddress>();
        }
    }
}
