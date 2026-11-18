using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using SpireParty.Core;

namespace SpireParty.GameAdapters;

/// <summary>
/// Widens the three lobby fields that cap the game at 4 players (findings → M3, four-player audit
/// rows 1–3), during a SpireParty session only:
///   StartRunLobbyPlayer.Serialize/Deserialize        slotId          WriteInt(x, 2) / ReadInt(2)
///   ClientLobbyJoinResponseMessage.Serialize/Deser.  playersInLobby  WriteList(x, 3) / ReadList(3)
///   LobbyBeginRunMessage.Serialize/Deserialize       playersInLobby  WriteList(x, 3) / ReadList(3)
/// A transpiler swaps the width constant right before each of those calls for a call to
/// <see cref="WireWidths"/>, which answers the vanilla width outside a session. Same approach as
/// RemoveMultiplayerPlayerLimit (CC0; there the widths are always wider), written for this build.
/// The self-check runs the same matcher on the game's own IL and requires exactly one match per
/// method, so a game update that changes any of them disables "more than 4 players" instead of
/// sending packets the other side cannot read.
/// </summary>
public sealed class WireFormatAdapter : GameAdapter
{
    public override string Name => "WireFormat";
    public override IReadOnlyList<string> Features => new[] { Features_.MoreThanFourPlayers };

    sealed record Site(MethodInfo? Method, string Label, Func<MethodInfo, bool> IsTargetCall, int VanillaWidth, MethodInfo Replacement);

    static readonly MethodInfo? WriteIntBits = AccessTools.Method(typeof(PacketWriter), nameof(PacketWriter.WriteInt), new[] { typeof(int), typeof(int) });
    static readonly MethodInfo? ReadIntBits = AccessTools.Method(typeof(PacketReader), nameof(PacketReader.ReadInt), new[] { typeof(int) });
    static readonly MethodInfo? WriteListDef = typeof(PacketWriter).GetMethods().FirstOrDefault(m =>
        m.Name == nameof(PacketWriter.WriteList) && m.IsGenericMethodDefinition && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(int));
    static readonly MethodInfo? ReadListDef = typeof(PacketReader).GetMethods().FirstOrDefault(m =>
        m.Name == nameof(PacketReader.ReadList) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(int));
    static readonly MethodInfo SlotBits = AccessTools.Method(typeof(WireWidths), nameof(WireWidths.SlotBits));
    static readonly MethodInfo ListBits = AccessTools.Method(typeof(WireWidths), nameof(WireWidths.ListBits));

    static bool Is(MethodInfo call, MethodInfo? target) => target != null && call == target;
    static bool IsGeneric(MethodInfo call, MethodInfo? definition) =>
        definition != null && call.IsGenericMethod && call.GetGenericMethodDefinition() == definition;

    static readonly Site[] Sites =
    {
        new(AccessTools.Method(typeof(StartRunLobbyPlayer), nameof(StartRunLobbyPlayer.Serialize)), "StartRunLobbyPlayer.Serialize",
            c => Is(c, WriteIntBits), WireWidths.VanillaSlotBits, SlotBits),
        new(AccessTools.Method(typeof(StartRunLobbyPlayer), nameof(StartRunLobbyPlayer.Deserialize)), "StartRunLobbyPlayer.Deserialize",
            c => Is(c, ReadIntBits), WireWidths.VanillaSlotBits, SlotBits),
        new(AccessTools.Method(typeof(ClientLobbyJoinResponseMessage), nameof(ClientLobbyJoinResponseMessage.Serialize)), "ClientLobbyJoinResponseMessage.Serialize",
            c => IsGeneric(c, WriteListDef), WireWidths.VanillaListBits, ListBits),
        new(AccessTools.Method(typeof(ClientLobbyJoinResponseMessage), nameof(ClientLobbyJoinResponseMessage.Deserialize)), "ClientLobbyJoinResponseMessage.Deserialize",
            c => IsGeneric(c, ReadListDef), WireWidths.VanillaListBits, ListBits),
        new(AccessTools.Method(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Serialize)), "LobbyBeginRunMessage.Serialize",
            c => IsGeneric(c, WriteListDef), WireWidths.VanillaListBits, ListBits),
        new(AccessTools.Method(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Deserialize)), "LobbyBeginRunMessage.Deserialize",
            c => IsGeneric(c, ReadListDef), WireWidths.VanillaListBits, ListBits),
    };

    /// <summary>Indexes of the width constants to replace: an ldc.i4 of the vanilla width directly before a target call.</summary>
    static List<int> Matches(IReadOnlyList<CodeInstruction> code, Site site)
    {
        var found = new List<int>();
        for (int i = 1; i < code.Count; i++)
        {
            if ((code[i].opcode == OpCodes.Call || code[i].opcode == OpCodes.Callvirt)
                && code[i].operand is MethodInfo call && site.IsTargetCall(call)
                && code[i - 1].LoadsConstant(site.VanillaWidth))
                found.Add(i - 1);
        }
        return found;
    }

    public override string? SelfCheck()
    {
        if (WriteIntBits == null || ReadIntBits == null || WriteListDef == null || ReadListDef == null)
            return "PacketWriter/PacketReader width overloads not found";
        foreach (var site in Sites)
        {
            if (site.Method == null) return $"method {site.Label} not found";
            var code = PatchProcessor.GetOriginalInstructions(site.Method);
            int n = Matches(code, site).Count;
            if (n != 1) return $"{site.Label}: expected one width-{site.VanillaWidth} field, found {n}";
        }
        return null;
    }

    public override void Apply(Harmony harmony)
    {
        var transpiler = new HarmonyMethod(typeof(WireFormatAdapter), nameof(Transpiler));
        foreach (var site in Sites) harmony.Patch(site.Method, transpiler: transpiler);
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        var site = Sites.First(s => s.Method == __originalMethod);
        foreach (int i in Matches(code, site))
        {
            var old = code[i];
            code[i] = new CodeInstruction(OpCodes.Call, site.Replacement) { labels = old.labels, blocks = old.blocks };
        }
        return code;
    }
}
