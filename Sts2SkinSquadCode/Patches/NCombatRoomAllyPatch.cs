using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Sts2SkinSquad.Patches;

/// <summary>
/// Adds the squad right after the game has created and positioned the real allies.
///
/// <c>CreateAllyNodes</c> is the correct seam: it runs inside <c>NCombatRoom._Ready</c> with
/// <c>_allyContainer</c> already resolved, and before <c>CreateEnemyNodes</c> — so the ally
/// container is live and nothing has read the final geometry yet. Positioning happens here
/// rather than later because <c>AdjustCreatureScaleForAspectRatio</c> runs afterwards.
/// </summary>
[HarmonyPatch(typeof(NCombatRoom), "CreateAllyNodes")]
public static class NCombatRoomAllyPatch
{
    public static void Postfix(NCombatRoom __instance)
    {
        try
        {
            SkinSquadService.PopulateSquad(__instance);
        }
        catch (Exception ex)
        {
            // Never let a cosmetic overlay take the combat room down with it.
            MainFile.Logger.Warn($"[{MainFile.ModId}] squad population failed: {ex.Message}");
        }
    }
}
