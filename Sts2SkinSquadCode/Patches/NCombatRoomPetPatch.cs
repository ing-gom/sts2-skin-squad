using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Sts2SkinSquad.Patches;

/// <summary>
/// Keeps each double's pets in sync with the real player's.
///
/// This exists because pets are NOT present when the squad is built: <c>CreateAllyNodes</c> runs
/// inside <c>NCombatRoom._Ready</c>, before combat setup, so a summoned pet like Necrobinder's
/// Osty only reaches the scene later via <c>AddCreature</c>. Verified in-game — a Necrobinder
/// combat reported "0 starting pet(s)" while an Osty was clearly on screen.
/// </summary>
[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.AddCreature))]
public static class NCombatRoomAddCreaturePatch
{
    public static void Postfix(Creature creature)
    {
        try
        {
            SkinSquadService.MirrorPet(creature);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] pet mirror failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Replays the real creature's animation triggers on its copies.
///
/// <c>SetAnimationTrigger</c> is the single funnel the game drives every creature animation
/// through (<c>_spineAnimator?.SetTrigger</c>), covering Idle / Attack / Cast / Hit / Dead /
/// Revive, so one hook here keeps the whole squad in step instead of leaving it in a permanent
/// idle while you fight.
/// </summary>
[HarmonyPatch(typeof(NCreature), nameof(NCreature.SetAnimationTrigger))]
public static class NCreatureAnimationTriggerPatch
{
    public static void Postfix(NCreature __instance, string trigger)
    {
        try
        {
            SkinSquadService.MirrorTrigger(__instance?.Entity, trigger);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] trigger mirror hook failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Frees the mirrored copies when the game removes the real pet, so a dead Osty doesn't leave
/// a row of ghosts standing next to the squad.
/// </summary>
[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.RemoveCreatureNode))]
public static class NCombatRoomRemoveCreaturePatch
{
    public static void Prefix(NCreature node)
    {
        try
        {
            SkinSquadService.RemoveMirroredPet(node?.Entity);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] pet un-mirror failed: {ex.Message}");
        }
    }
}
