using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Sts2SkinSquad;

/// <summary>
/// One party member for layout purposes — either the real, controllable player
/// (<see cref="Node"/> is its <see cref="NCreature"/>) or one of our visual-only doubles
/// (<see cref="Node"/> is a bare <see cref="NCreatureVisuals"/> with no combat entity).
///
/// Both cases position identically: <c>NCreature</c> parks its own <c>Visuals</c> child at
/// local (0,0) (see <c>NCreature._Ready</c>), so placing a bare <c>NCreatureVisuals</c> at the
/// coordinates the game would have given the <c>NCreature</c> puts the body in the same spot.
/// The two node types don't share a base that exposes Position, hence <see cref="SetPosition"/>.
/// </summary>
internal sealed class PartySlot
{
    public PartySlot(Node node, NCreatureVisuals visuals, bool isLocalReal, Creature? entity)
    {
        Node = node;
        Visuals = visuals;
        IsLocalReal = isLocalReal;
        Entity = entity;
    }

    /// <summary>Node whose Position places this member: NCreature (real) or NCreatureVisuals (double).</summary>
    public Node Node { get; }

    /// <summary>Always the spine actor — the source of <c>Bounds</c> and of Modulate tinting.</summary>
    public NCreatureVisuals Visuals { get; }

    /// <summary>True only for the one real player the user actually controls.</summary>
    public bool IsLocalReal { get; }

    /// <summary>Combat entity, or null for a double (doubles deliberately have none).</summary>
    public Creature? Entity { get; }

    public List<PartySlot> Pets { get; } = new();

    /// <summary>
    /// Animation state machine for a double, built from the same <c>GenerateAnimator</c> the game
    /// uses for real creatures so triggers blend identically. Null for the real player (the game
    /// already drives it) and for any actor whose skeleton lacks the expected animations.
    /// </summary>
    public CreatureAnimator? Animator { get; set; }

    /// <summary>
    /// A plain Godot <see cref="AnimationPlayer"/> inside the actor, for characters that are not
    /// Spine rigs at all.
    ///
    /// The game only ever drives creatures through Spine, so a character mod shipping sprite art
    /// animates it itself — measured on SlayTheUniverse's characters, whose visuals are a Sprite2D
    /// plus an AnimationPlayer named "OddmeltAnim". Copies of those can still be made to follow, but
    /// through this rather than through a CreatureAnimator.
    /// </summary>
    public AnimationPlayer? Player { get; set; }

    /// <summary>
    /// Necrobinder's Osty, which gets hand-placed out to the owner's right instead of going through
    /// the generic pet loop.
    ///
    /// Set at construction rather than derived from <see cref="Entity"/>: a mirrored pet carries no
    /// entity at all, and a squad member's Osty now takes the same hand-placement the played
    /// character's does, so "is this an Osty" has to be answerable for a copy too.
    /// </summary>
    public bool IsOsty { get; init; }

    /// <summary>Layout width, matching what the game reads (<c>Visuals.Bounds.Size.X</c>).</summary>
    public float Width => GodotObject.IsInstanceValid(Visuals) && Visuals.Bounds != null
        ? Visuals.Bounds.Size.X
        : 0f;

    public Vector2 Position
    {
        get => Node switch
        {
            Control c => c.Position,
            Node2D n => n.Position,
            _ => Vector2.Zero,
        };
        set => SetPosition(value);
    }

    private void SetPosition(Vector2 p)
    {
        switch (Node)
        {
            case Control c: c.Position = p; break;
            case Node2D n: n.Position = p; break;
        }
    }

    /// <summary>Grey-out used by the game for party members behind the front row.</summary>
    public void Dim(Color color)
    {
        if (GodotObject.IsInstanceValid(Visuals)) Visuals.Modulate = color;
    }

    /// <summary>Matches the game, which hides the bounds marker on non-local players' pets.</summary>
    public void HideBounds() => Visuals.HideBoundsMarker();
}
