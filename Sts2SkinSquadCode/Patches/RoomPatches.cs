using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Sts2SkinSquad.Patches;

/// <summary>
/// Adds the squad to the campfire once the room has built its own single character.
/// </summary>
[HarmonyPatch(typeof(NRestSiteRoom), "_Ready")]
public static class NRestSiteRoomPatch
{
    public static void Postfix(NRestSiteRoom __instance)
    {
        try
        {
            SquadRooms.PopulateRestSite(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] rest site hook failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Skips <c>NRestSiteCharacter._Ready</c> for the decorative actors this mod creates.
///
/// That method is written for a real party member: it dereferences <c>Player.RunState</c> for the
/// act loop and branches on <c>Player.Character is Necrobinder</c> to set up campfire flames. Our
/// actors have no Player, and their character need not match the one being played — an Ironclad
/// double beside a Necrobinder player would send it looking for <c>%NecroFire</c> on the Ironclad
/// scene and throw. <see cref="SquadRooms.PopulateRestSite"/> does the parts that matter (the act
/// loop) itself; the rest is interaction plumbing a decoration never uses.
/// </summary>
[HarmonyPatch(typeof(NRestSiteCharacter), "_Ready")]
public static class NRestSiteCharacterReadyPatch
{
    public static bool Prefix(NRestSiteCharacter __instance)
    {
        try
        {
            return !__instance.Name.ToString().StartsWith(SquadRooms.NodePrefix, StringComparison.Ordinal);
        }
        catch
        {
            return true;   // anything unexpected: let the game run its own _Ready
        }
    }
}

/// <summary>
/// Adds the squad to the shop after the room has placed the real player.
///
/// <c>AfterRoomIsLoaded</c> rather than <c>_Ready</c>: that is where the merchant room instantiates
/// its character visuals and runs the grid, so hooking earlier would find nothing to extend.
/// </summary>
[HarmonyPatch(typeof(NMerchantRoom), "AfterRoomIsLoaded")]
public static class NMerchantRoomPatch
{
    public static void Postfix(NMerchantRoom __instance)
    {
        try
        {
            SquadRooms.PopulateShop(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] shop hook failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Brings the squad along to the game-over screen.
///
/// That screen lifts the fighters onto its own layer so they stay visible above the backstop, but it
/// builds its list from <c>NCombatRoom.CreatureNodes</c> — which by design never contains our
/// entity-less copies. The result was the reported bug: on death only the real character played the
/// death motion, because everyone else was left behind the overlay.
/// </summary>
[HarmonyPatch(typeof(NGameOverScreen), "MoveCreaturesToDifferentLayerAndDisableUi")]
public static class NGameOverScreenPatch
{
    public static void Postfix(NGameOverScreen __instance)
    {
        try
        {
            SkinSquadService.FollowToGameOver(__instance._creatureContainer);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] game-over hook failed: {ex.Message}");
        }
    }
}
