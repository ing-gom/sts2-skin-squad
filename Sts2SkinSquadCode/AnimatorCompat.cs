using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace Sts2SkinSquad;

/// <summary>
/// <c>CharacterModel.GenerateAnimator</c>, which does not have the same parameter list on every
/// game branch this one DLL has to serve.
///
/// ★v0.111.0 gave characters a low-health idle, and the animator needs a creature to ask about it:
/// <c>GenerateAnimator(MegaSprite)</c> became <c>GenerateAnimator(MegaSprite, Creature)</c>, with
/// the one-argument form REMOVED. Unlike the return-type change <see cref="SpineCompat"/> exists
/// for, this one does not compile silently — but it does mean a DLL compiled against either branch
/// throws <see cref="MissingMethodException"/> on the other, and the throw lands when the method
/// holding the call is JITted, i.e. outside the <c>try</c> that surrounds the call itself. That is
/// why the mod looked completely dead on public-beta while still loading and logging fine: every
/// double failed to spawn one frame after its body had already been parented.
///
/// <c>MonsterModel.GenerateAnimator(MegaSprite)</c> was left alone, so the pet path still calls it
/// directly.
///
/// ★Reflection here, not a cached delegate. This runs once per squad member per room (at most a
/// handful of times per combat), so it is nowhere near the animation hot path
/// <see cref="SpineCompat"/> had to bind for — and <c>MethodInfo.Invoke</c> dispatches virtually,
/// which an open delegate over a specific signature would have to be built carefully to preserve.
/// <c>GenerateAnimator</c> is <c>virtual</c> and no vanilla character overrides it, but a custom
/// character mod may, and this mod puts custom characters in squad slots.
/// </summary>
internal static class AnimatorCompat
{
    /// <summary>Signature on public-beta v0.111.0 and later.</summary>
    private static readonly MethodInfo? WithCreature;

    /// <summary>Signature on public v0.107.1 and earlier.</summary>
    private static readonly MethodInfo? SpriteOnly;

    static AnimatorCompat()
    {
        WithCreature = typeof(CharacterModel).GetMethod(
            "GenerateAnimator",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(MegaSprite), typeof(Creature) },
            modifiers: null);

        if (WithCreature != null) return;

        SpriteOnly = typeof(CharacterModel).GetMethod(
            "GenerateAnimator",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(MegaSprite) },
            modifiers: null);

        if (SpriteOnly == null)
        {
            MainFile.Logger.Warn(
                $"[{MainFile.ModId}] CharacterModel.GenerateAnimator not found in any known shape on " +
                "this game build; squad members will fall back to hard-cut trigger animations.");
        }
    }

    /// <summary>
    /// Builds a character's animation state machine over <paramref name="sprite"/>.
    ///
    /// <paramref name="lowHealthSource"/> is only read by the branch that asks for a creature, and
    /// only to decide between the normal and the low-health idle. A double has no creature entity
    /// of its own — that is the whole point of it — and the game's predicate dereferences the
    /// argument immediately (<c>creature.GetHpPercentRemaining() &lt;= 0.25</c>), so null is not an
    /// option: pass the controlled character's own creature. The game re-evaluates the predicate on
    /// every transition, so the squad drops into the low-health idle alongside you rather than
    /// freezing at whatever was true when the room loaded.
    /// </summary>
    /// <returns>The animator, or null when neither shape is present.</returns>
    public static CreatureAnimator? Build(CharacterModel character, MegaSprite sprite, Creature lowHealthSource)
    {
        if (WithCreature != null)
            return WithCreature.Invoke(character, new object[] { sprite, lowHealthSource }) as CreatureAnimator;

        return SpriteOnly?.Invoke(character, new object[] { sprite }) as CreatureAnimator;
    }

    /// <summary>
    /// True when this build's animator can ask for the low-health idle, so callers can warn about a
    /// rig that has no <c>low_health_loop</c> to give it.
    /// </summary>
    public static bool HasLowHealthIdle => WithCreature != null;

    /// <summary>The clip v0.111.0's animator selects while the creature it was given is at or below
    /// a quarter health.</summary>
    public const string LowHealthAnimation = "low_health_loop";
}
