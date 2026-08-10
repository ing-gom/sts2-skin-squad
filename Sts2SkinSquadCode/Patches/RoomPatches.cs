using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Sts2SkinSquad.Patches;

/// <summary>
/// Adds the squad to the campfire once the room has built its own single character.
///
/// ★A FINALIZER, NOT A POSTFIX. <c>_Ready</c> creates the character containers and fills slot 0
/// before it calls <c>UpdateRestSiteOptions()</c>, which asks every registered rest-site option
/// whether it is enabled — including options belonging to other mods. One of those throwing takes
/// the rest of <c>_Ready</c> with it, and a postfix never runs, so the campfire silently shows one
/// character while combat and the shop show the whole squad. Observed on public-beta v0.110.1,
/// where a sister mod built against v0.107.1 threw <c>MissingMethodException</c> from its reforge
/// option and cost this screen entirely.
///
/// A finalizer runs either way, and everything the squad needs already exists by then. The
/// original exception is returned untouched — this makes the squad robust to a neighbour's
/// failure, it does not hide it.
/// </summary>
[HarmonyPatch(typeof(NRestSiteRoom), "_Ready")]
public static class NRestSiteRoomPatch
{
    public static Exception? Finalizer(NRestSiteRoom __instance, Exception? __exception)
    {
        try
        {
            SquadRooms.PopulateRestSite(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] rest site hook failed: {ex.Message}");
        }

        return __exception;
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
