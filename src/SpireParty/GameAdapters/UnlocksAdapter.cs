using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using SpireParty.Core;

namespace SpireParty.GameAdapters;

/// <summary>
/// Applies the party's unlock setting (<see cref="PartyUnlocks"/>) during a session, v0.113.1:
/// <list type="bullet">
/// <item><c>StartRunLobby.AddLocalHostPlayer</c> (prefix): the host's lobby player.</item>
/// <item><c>ClientLobbyJoinRequestMessage.Serialize</c> (prefix): what a client sends to join the lobby.</item>
/// <item><c>SaveManager.GenerateUnlockStateFromProgress</c> (postfix): local screens such as the character
/// buttons, so the picker shows what the lobby allows.</item>
/// <item><c>NAscensionPanel.Initialize</c> (postfix): the host's ascension limit, which the panel reads
/// from local progress.</item>
/// </list>
/// Nothing is written to the profile. Outside a session the game is left alone.
/// </summary>
public sealed class UnlocksAdapter : GameAdapter
{
    public override string Name => "Unlocks";
    // If this cannot be applied, each player brings their own profile's unlocks, as in vanilla co-op.
    public override IReadOnlyList<string> Features => Array.Empty<string>();

    static readonly Type[] HostArgs = { typeof(UnlockState), typeof(int), typeof(int) };

    public override string? SelfCheck() =>
        Require(typeof(StartRunLobby), nameof(StartRunLobby), nameof(StartRunLobby.AddLocalHostPlayer), HostArgs)
        ?? Require(typeof(ClientLobbyJoinRequestMessage), nameof(ClientLobbyJoinRequestMessage), nameof(ClientLobbyJoinRequestMessage.Serialize))
        ?? Require(typeof(SaveManager), nameof(SaveManager), nameof(SaveManager.GenerateUnlockStateFromProgress), Type.EmptyTypes)
        ?? Require(typeof(NAscensionPanel), nameof(NAscensionPanel), nameof(NAscensionPanel.Initialize), new[] { typeof(MultiplayerUiMode), typeof(GameMode) })
        ?? Require(typeof(NAscensionPanel), nameof(NAscensionPanel), nameof(NAscensionPanel.SetMaxAscension), new[] { typeof(int) })
        ?? Require(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened), Type.EmptyTypes)
        ?? MissingField(typeof(NCharacterSelectScreen), "_charButtonContainer")
        ?? MissingField(typeof(NCharacterSelectButton), "_isLocked")
        ?? MissingField(typeof(NCharacterSelectButton), "_lock")
        ?? MissingField(typeof(NCharacterSelectButton), "_icon")
        ?? (AccessTools.Field(typeof(ClientLobbyJoinRequestMessage), nameof(ClientLobbyJoinRequestMessage.unlockState)) == null
            ? "field ClientLobbyJoinRequestMessage.unlockState not found" : null);

    public override void Apply(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(StartRunLobby), nameof(StartRunLobby.AddLocalHostPlayer), HostArgs),
            prefix: new HarmonyMethod(typeof(UnlocksAdapter), nameof(HostPlayerPrefix)));
        harmony.Patch(AccessTools.Method(typeof(ClientLobbyJoinRequestMessage), nameof(ClientLobbyJoinRequestMessage.Serialize)),
            prefix: new HarmonyMethod(typeof(UnlocksAdapter), nameof(JoinRequestPrefix)));
        harmony.Patch(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.GenerateUnlockStateFromProgress), Type.EmptyTypes),
            postfix: new HarmonyMethod(typeof(UnlocksAdapter), nameof(LocalUnlocksPostfix)));
        harmony.Patch(AccessTools.Method(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened), Type.EmptyTypes),
            postfix: new HarmonyMethod(typeof(UnlocksAdapter), nameof(CharacterScreenOpenedPostfix)));
        harmony.Patch(AccessTools.Method(typeof(NAscensionPanel), nameof(NAscensionPanel.Initialize), new[] { typeof(MultiplayerUiMode), typeof(GameMode) }),
            postfix: new HarmonyMethod(typeof(UnlocksAdapter), nameof(AscensionPanelPostfix)));
    }

    static string? MissingField(Type type, string field) => AccessTools.Field(type, field) == null ? $"field {type.Name}.{field} not found" : null;

    static UnlockPolicy? Policy => SpirePartySession.IsActive ? PartyUnlocks.Current : null;

    static void HostPlayerPrefix(ref UnlockState unlocks, ref int maxStandardAscension, ref int maxBlitzAscension)
    {
        if (Policy is not { } p) return;
        unlocks = UnlockState.FromSerializable(p.Unlocks);
        maxStandardAscension = p.MaxStandardAscension;
        maxBlitzAscension = p.MaxBlitzAscension;
    }

    static void JoinRequestPrefix(ref ClientLobbyJoinRequestMessage __instance)
    {
        if (Policy is not { } p) return;
        __instance.unlockState = p.Unlocks;
        __instance.maxStandardAscension = p.MaxStandardAscension;
        __instance.maxBlitzAscension = p.MaxBlitzAscension;
    }

    static void LocalUnlocksPostfix(ref UnlockState __result)
    {
        if (Policy is { } p) __result = UnlockState.FromSerializable(p.Unlocks);
    }

    static void AscensionPanelPostfix(NAscensionPanel __instance, MultiplayerUiMode mode, GameMode gameMode)
    {
        if (Policy is not { } p || mode != MultiplayerUiMode.Host) return;
        __instance.SetMaxAscension(gameMode == GameMode.Blitz ? p.MaxBlitzAscension : p.MaxStandardAscension);
    }

    static readonly AccessTools.FieldRef<NCharacterSelectScreen, Control> ButtonContainer =
        AccessTools.FieldRefAccess<NCharacterSelectScreen, Control>("_charButtonContainer");
    static readonly AccessTools.FieldRef<NCharacterSelectButton, bool> IsLockedField =
        AccessTools.FieldRefAccess<NCharacterSelectButton, bool>("_isLocked");
    static readonly AccessTools.FieldRef<NCharacterSelectButton, TextureRect> LockIcon =
        AccessTools.FieldRefAccess<NCharacterSelectButton, TextureRect>("_lock");
    static readonly AccessTools.FieldRef<NCharacterSelectButton, TextureRect> Icon =
        AccessTools.FieldRefAccess<NCharacterSelectButton, TextureRect>("_icon");

    /// <summary>
    /// The character select screen is built once, at the main menu, and reused. Its buttons' locks come from
    /// that moment, and the game only ever unlocks them later (<c>UnlockIfPossible</c>, which also never
    /// unlocks the random button). So on every open, set each button's lock from the current unlocks: the
    /// party's during a session, the profile's otherwise. This also locks again, after a session, what the
    /// party had unlocked (hard rule 8).
    /// </summary>
    static void CharacterScreenOpenedPostfix(NCharacterSelectScreen __instance)
    {
        var unlocks = SaveManager.Instance.GenerateUnlockStateFromProgress(); // patched during a session
        foreach (var button in ButtonContainer(__instance).GetChildren().OfType<NCharacterSelectButton>())
        {
            bool locked;
            if (button.IsRandom) locked = ModelDb.AllCharacters.Any(c => !unlocks.Characters.Contains(c));
            else if (button.Character is RandomCharacter or RotatingCharacter || button.Character == null) continue;
            else locked = !unlocks.Characters.Contains(button.Character);
            if (locked == button.IsLocked) continue;

            IsLockedField(button) = locked;
            LockIcon(button).Visible = locked;
            Icon(button).Texture = locked ? button.Character.CharacterSelectLockedIcon : button.Character.CharacterSelectIcon;
            if (!locked) button.Enable();
        }
    }
}
