using System.Linq;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Unlocks;

namespace SpireParty.Core;

/// <summary>What every player in a party run enters the lobby with (DESIGN → Unlocks for party runs).</summary>
/// <param name="Mode"><see cref="PartyUnlocks.Everything"/> or <see cref="PartyUnlocks.MirrorHost"/>.</param>
public sealed record UnlockPolicy(string Mode, SerializableUnlockState Unlocks, int MaxStandardAscension, int MaxBlitzAscension);

/// <summary>
/// The party's unlock setting, applied on host and clients alike (<c>UnlocksAdapter</c>), so every player
/// enters the lobby with the same unlocks and ascension limit. Without it each client would bring its own
/// profile's unlocks (findings → Unlocks in co-op). Nothing here writes the profile; mirrorHost only reads it.
/// </summary>
public static class PartyUnlocks
{
    public const string Everything = "everything";
    public const string MirrorHost = "mirrorHost";

    /// <summary>
    /// Run count for "everything". Not the game's own <c>UnlockState.all</c> (999999999 runs): the lobby
    /// sends the count in 16 bits. Any value above 0 turns off first-run helpers, as for a seasoned profile.
    /// </summary>
    public const int EverythingRuns = 1000;

    /// <summary>The policy of the running session; null outside a session (the game is left alone).</summary>
    public static UnlockPolicy? Current { get; private set; }

    public static void Set(UnlockPolicy? policy) => Current = policy;

    public static UnlockState AllUnlocked() =>
        new(EpochModel.AllEpochIds, ModelDb.AllEncounters.Select(e => e.Id), EverythingRuns);

    public static UnlockPolicy EverythingPolicy() =>
        new(Everything, AllUnlocked().ToSerializable(), AscensionManager.maxAscensionAllowed, AscensionManager.maxAscensionAllowedBlitz);

    /// <summary>The local profile's unlocks, read only. On the host this is the host's modded profile.</summary>
    public static UnlockPolicy FromLocalProgress()
    {
        var progress = SaveManager.Instance.Progress;
        return new(MirrorHost, new UnlockState(progress).ToSerializable(), progress.MaxMultiplayerAscension, progress.MaxBlitzAscension);
    }

    /// <summary>The host's policy for a party: party.json's setting, unknown values meaning "everything".</summary>
    public static UnlockPolicy ForHost(PartyFile party) => party.Unlocks == MirrorHost ? FromLocalProgress() : EverythingPolicy();

    /// <summary>A client's policy from the welcome; "mirrorHost" without the host's unlocks falls back to everything.</summary>
    public static UnlockPolicy ForClient(string mode, string? hostUnlocksJson, int hostMaxStandard, int hostMaxBlitz)
    {
        if (mode == MirrorHost && hostUnlocksJson != null && FromJson(hostUnlocksJson) is { } host)
            return new(MirrorHost, host, hostMaxStandard, hostMaxBlitz);
        return EverythingPolicy();
    }

    /// <summary>Unlocks for a player added in the roster editor (runs on the host, outside a session).</summary>
    public static UnlockState ForNewPlayer(PartyFile party) =>
        UnlockState.FromSerializable(ForHost(party).Unlocks);

    public static string ToJson(SerializableUnlockState unlocks) =>
        System.Text.Json.JsonSerializer.Serialize(unlocks, JsonSerializationUtility.GetTypeInfo<SerializableUnlockState>());

    public static SerializableUnlockState? FromJson(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize(json, JsonSerializationUtility.GetTypeInfo<SerializableUnlockState>());
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
