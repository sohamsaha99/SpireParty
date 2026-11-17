using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpireParty.Core;

/// <summary>A host this machine joined before (shown in the Join menu). Tokens are never stored here.</summary>
public sealed record RecentHost(string Address, int Port, string SeatId);

/// <summary>
/// Machine settings, <c>user://mods/SpireParty/config.json</c>.
/// Defaults: <c>docs/DESIGN.md</c> → Defaults and configuration.
/// </summary>
public sealed class MachineConfig
{
    public const int DefaultGamePort = 33771;
    public const int DefaultWebPort = 8771;
    public const int MaxRecentHosts = 8;

    public int GamePort { get; set; } = DefaultGamePort;
    public int WebPort { get; set; } = DefaultWebPort;
    /// <summary>"auto" = the Tailscale or Meshnet interface; otherwise a local IP address.</summary>
    public string ListenAddress { get; set; } = "auto";
    public bool BrowserSeats { get; set; } = true;
    public int BackupsKept { get; set; } = 20;
    public List<RecentHost> RecentHosts { get; set; } = new();

    public static MachineConfig Parse(string? json, out List<string> warnings)
    {
        var j = ForgivingJson.Parse(json, "config.json");
        var c = new MachineConfig
        {
            GamePort = j.Int("gamePort", DefaultGamePort, 1, 65535),
            WebPort = j.Int("webPort", DefaultWebPort, 1, 65535),
            ListenAddress = j.String("listenAddress", "auto").Trim(),
            BrowserSeats = j.Bool("browserSeats", true),
            BackupsKept = j.Int("backupsKept", 20, 1, 100),
        };
        if (c.ListenAddress.Length == 0) c.ListenAddress = "auto";
        foreach (var h in j.Objects("recentHosts"))
        {
            var address = h.String("address", "").Trim();
            var seat = h.String("seatId", "").Trim();
            if (address.Length == 0) continue;
            c.RecentHosts.Add(new RecentHost(address, h.Int("port", DefaultGamePort, 1, 65535), seat));
        }
        c.RecentHosts = c.RecentHosts.Take(MaxRecentHosts).ToList();
        warnings = j.Warnings;
        return c;
    }

    /// <summary>Puts this host first, removes duplicates and keeps the list short.</summary>
    public void Remember(RecentHost host)
    {
        RecentHosts.RemoveAll(h => h.Address == host.Address && h.Port == host.Port && h.SeatId == host.SeatId);
        RecentHosts.Insert(0, host);
        if (RecentHosts.Count > MaxRecentHosts) RecentHosts.RemoveRange(MaxRecentHosts, RecentHosts.Count - MaxRecentHosts);
    }

    public const string FileName = "config.json";

    public static MachineConfig Load(string spirePartyDataDir, out List<string> warnings)
    {
        string path = System.IO.Path.Combine(spirePartyDataDir, FileName);
        string? json = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
        return Parse(json, out warnings);
    }

    public void Save(string spirePartyDataDir)
    {
        System.IO.Directory.CreateDirectory(spirePartyDataDir);
        string path = System.IO.Path.Combine(spirePartyDataDir, FileName);
        System.IO.File.WriteAllText(path + ".tmp", ToJson());
        System.IO.File.Move(path + ".tmp", path, overwrite: true);
    }

    public string ToJson()
    {
        var o = new JsonObject
        {
            ["gamePort"] = GamePort,
            ["webPort"] = WebPort,
            ["listenAddress"] = ListenAddress,
            ["browserSeats"] = BrowserSeats,
            ["backupsKept"] = BackupsKept,
            ["recentHosts"] = new JsonArray(RecentHosts.Select(h => (JsonNode)new JsonObject
            {
                ["address"] = h.Address,
                ["port"] = h.Port,
                ["seatId"] = h.SeatId,
            }).ToArray()),
        };
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
