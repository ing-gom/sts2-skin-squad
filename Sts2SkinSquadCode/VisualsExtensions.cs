using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Sts2SkinSquad;

internal static class VisualsExtensions
{
    /// <summary>
    /// Hides the bounds marker, matching what the game does for pets that don't belong to the
    /// local player. Safe on a freed or half-built node.
    /// </summary>
    public static void HideBoundsMarker(this NCreatureVisuals visuals)
    {
        if (GodotObject.IsInstanceValid(visuals) && visuals.Bounds != null) visuals.Bounds.Visible = false;
    }
}
