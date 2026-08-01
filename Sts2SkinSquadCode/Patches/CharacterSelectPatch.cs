using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using Sts2SkinSquad.Ui;

namespace Sts2SkinSquad.Patches;

/// <summary>
/// Adds the squad button once the character-select screen has built itself.
///
/// Deferred rather than immediate: the button is positioned relative to Embark, and Embark's
/// <c>Size</c> is not final until the screen's layout pass has run.
/// </summary>
[HarmonyPatch(typeof(NCharacterSelectScreen), "_Ready")]
public static class CharacterSelectScreenPatch
{
    public static void Postfix(NCharacterSelectScreen __instance) =>
        Callable.From(() => Attach(__instance)).CallDeferred();

    private static void Attach(NCharacterSelectScreen screen)
    {
        try
        {
            SquadButton.Attach(screen);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] character select hook failed: {ex.Message}");
        }
    }
}
