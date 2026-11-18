using System;
using System.IO;
using System.Linq;

using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace SpireParty.Core;

/// <summary>
/// M2 task 2.1 spike, behind the --spireparty-addseat dev switch: adds a new seat and player to a party's
/// saved run. Replaced by the roster editor (2.2 to 2.8). Copies run.save and party.json aside first.
/// </summary>
public static class AddSeatSpike
{
    /// <param name="args">&lt;party&gt;,&lt;seat&gt;,&lt;character&gt;,&lt;token&gt;</param>
    public static string Run(string args, string spirePartyDataDir)
    {
        string[] a = args.Split(',').Select(x => x.Trim()).ToArray();
        if (a.Length != 4) throw new ArgumentException("--spireparty-addseat=<party>,<seat>,<character>,<token>");
        string folder = SeatRules.PartyFolderName(a[0]);
        string seatId = SeatRules.Normalize(a[1]);
        if (!SeatRules.IsValidSeatId(seatId)) throw new ArgumentException($"bad seat ID \"{a[1]}\"");
        var character = NewPlayer.FindCharacter(a[2])
                        ?? throw new ArgumentException($"unknown character \"{a[2]}\"");

        var store = new PartyStore(spirePartyDataDir);
        var party = store.Load(folder);
        string savePath = store.RunSavePath(folder);
        if (!File.Exists(savePath)) throw new InvalidOperationException($"party \"{folder}\" has no run in progress");

        string json = File.ReadAllText(savePath);
        var run = JsonSerializationUtility.FromJson<SerializableRun>(json).SaveData
                  ?? throw new InvalidOperationException("run.save could not be read");

        ulong netId = RosterEdit.PlayerIdForAdd(run, party, seatId, out string? refusal)
                      ?? throw new InvalidOperationException(refusal);

        // Spike only: the new player gets the host's unlock state (task 2.7 replaces this).
        var host = run.Players.First(p => p.NetId == SeatRules.HostPlayerId);
        var unlocks = UnlockState.FromSerializable(host.UnlockState);
        var player = NewPlayer.Build(json, character, unlocks, netId, CatchUp.DreadBanes(run));
        CatchUp.Apply(player, run.Players, party.CatchUpGold, party.CatchUpMaxHp);

        var result = RosterEdit.Add(run, party, seatId, seatId, player);
        if (!result.Ok) throw new InvalidOperationException(result.Refusal);
        party.Seats.First(s => s.SeatId == seatId).TokenHash = SeatTokens.Hash(a[3]);

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        File.Copy(savePath, savePath + $".spike-{stamp}", overwrite: false);
        File.Copy(Path.Combine(store.PartyDir(folder), PartyStore.PartyFileName),
                  Path.Combine(store.PartyDir(folder), $"party.json.spike-{stamp}"), overwrite: false);
        File.WriteAllBytes(savePath, PartyRunSave.ToBytes(run));
        store.Save(folder, party);

        return $"seat \"{seatId}\" = player {netId} ({character.Id}): hp {player.CurrentHp}/{player.MaxHp}, gold {player.Gold}, " +
               $"deck {player.Deck.Count}, relics {player.Relics.Count}; backfilled {result.Backfilled} history entries; " +
               $"players now {string.Join(", ", run.Players.Select(p => p.NetId))}";
    }
}
