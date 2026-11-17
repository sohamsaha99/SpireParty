// M0 save-surgery spike (throwaway). Works only on COPIES of saves, never on a profile folder.
//
//   roundtrip <in> <out>                 load + write unchanged; reports whether bytes are identical
//   scan      <in>                       list every player reference in the save
//   bench     <in> <out> <netId> <json>  remove a player; their SerializablePlayer goes to <json>
//   restore   <in> <out> <json>          append the benched player; backfill per-floor history
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

static class Program
{
    static int Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "roundtrip" => RoundTrip(args[1], args[2]),
                "scan" => Scan(Load(args[1])),
                "bench" => Bench(args[1], args[2], ulong.Parse(args[3]), args[4]),
                "restore" => Restore(args[1], args[2], args[3]),
                "strip-history" => StripHistory(args[1], args[2], ulong.Parse(args[3])),
                _ => Usage(),
            };
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"FAILED: {e}");
            return 1;
        }
    }

    static int Usage()
    {
        Console.Error.WriteLine("usage: roundtrip|scan|bench|restore ... (see Program.cs)");
        return 2;
    }

    static SerializableRun Load(string path) =>
        JsonSerializer.Deserialize(File.ReadAllBytes(path), JsonSerializationUtility.GetTypeInfo<SerializableRun>())
        ?? throw new InvalidDataException("save deserialized to null");

    static void Write(SerializableRun run, string path)
    {
        using var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, run, JsonSerializationUtility.GetTypeInfo<SerializableRun>());
        File.WriteAllBytes(path, stream.ToArray());
    }

    static int RoundTrip(string input, string output)
    {
        Write(Load(input), output);
        bool same = File.ReadAllBytes(input).AsSpan().SequenceEqual(File.ReadAllBytes(output));
        Console.WriteLine(same ? "roundtrip: byte-identical" : "roundtrip: DIFFERENT bytes");
        return same ? 0 : 3;
    }

    static int Scan(SerializableRun run)
    {
        Console.WriteLine($"players (slot order): {string.Join(", ", run.Players.Select(p => p.NetId))}");
        var ids = run.Players.Select(p => p.NetId).ToHashSet();

        var extra = run.PreFinishedRoom?.ExtraRewards;
        Console.WriteLine($"pre_finished_room: {(run.PreFinishedRoom == null ? "none" : "present")}; " +
                          $"extra_rewards keys: {(extra == null ? "-" : string.Join(", ", extra.Keys))}");

        int floor = 0;
        foreach (var entry in run.MapPointHistory.SelectMany(act => act))
        {
            floor++;
            var present = entry.PlayerStats.Select(s => s.PlayerId).ToList();
            var missing = ids.Except(present).ToList();
            var unknown = present.Except(ids).ToList();
            Console.WriteLine($"history floor {floor}: players [{string.Join(", ", present)}]" +
                              (missing.Count > 0 ? $"  MISSING {string.Join(", ", missing)}" : "") +
                              (unknown.Count > 0 ? $"  NOT-IN-RUN {string.Join(", ", unknown)}" : ""));
        }

        var drawings = run.MapDrawings?.drawings.Select(d => d.playerId) ?? [];
        Console.WriteLine($"map drawings for: {string.Join(", ", drawings)}");
        return 0;
    }

    static int Bench(string input, string output, ulong netId, string benchFile)
    {
        var run = Load(input);
        var player = run.Players.SingleOrDefault(p => p.NetId == netId)
                     ?? throw new InvalidOperationException($"player {netId} not in save");
        if (run.Players.Count < 2)
            throw new InvalidOperationException("refusing: cannot bench the last player");

        // Fail closed: unclaimed per-player rewards would be lost or orphaned.
        if (run.PreFinishedRoom?.ExtraRewards.TryGetValue(netId, out var rewards) == true && rewards.Count > 0)
            throw new InvalidOperationException($"refusing: player {netId} has {rewards.Count} unclaimed extra rewards in pre_finished_room");

        File.WriteAllText(benchFile, JsonSerializer.Serialize(player, JsonSerializationUtility.GetTypeInfo<SerializablePlayer>()), Encoding.UTF8);
        run.Players.Remove(player);
        run.PreFinishedRoom?.ExtraRewards.Remove(netId);
        // History entries and map drawings for this player are kept (the game tolerates both).
        Write(run, output);
        Console.WriteLine($"benched {netId} -> {benchFile}; players now: {string.Join(", ", run.Players.Select(p => p.NetId))}");
        return 0;
    }

    // Test helper: simulate a player who was absent for every recorded floor (joined later).
    static int StripHistory(string input, string output, ulong netId)
    {
        var run = Load(input);
        int removed = run.MapPointHistory.SelectMany(act => act).Sum(e => e.PlayerStats.RemoveAll(s => s.PlayerId == netId));
        Write(run, output);
        Console.WriteLine($"removed {removed} history entries for {netId}");
        return 0;
    }

    static int Restore(string input, string output, string benchFile)
    {
        var run = Load(input);
        var player = JsonSerializer.Deserialize(File.ReadAllText(benchFile), JsonSerializationUtility.GetTypeInfo<SerializablePlayer>())
                     ?? throw new InvalidDataException("bench file deserialized to null");
        if (run.Players.Any(p => p.NetId == player.NetId))
            throw new InvalidOperationException($"refusing: player {player.NetId} already in the run");

        run.Players.Add(player); // append only: existing slot order must not change
        int backfilled = 0;
        foreach (var entry in run.MapPointHistory.SelectMany(act => act))
        {
            if (entry.PlayerStats.All(s => s.PlayerId != player.NetId))
            {
                entry.PlayerStats.Add(new PlayerMapPointHistoryEntry { PlayerId = player.NetId });
                backfilled++;
            }
        }
        Write(run, output);
        Console.WriteLine($"restored {player.NetId}; backfilled {backfilled} history entries; players now: {string.Join(", ", run.Players.Select(p => p.NetId))}");
        return 0;
    }
}
