using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Sts2SkinSquad;

/// <summary>
/// Keeps the squad playing whatever the controlled character is playing, driven by the spine
/// runtime's own <c>animation_started</c> signal.
///
/// Why this exists at all: the trigger hook covers the normal path (attack, cast, hurt) but death
/// came out wrong in practice — the copies stood in idle while the real character died. Rather than
/// keep guessing which call path was missed, this listens to the observable truth: an animation
/// actually starting on the real character's sprite. Whatever the game does, by any route, the squad
/// follows.
///
/// Signal-driven, not polled. Polling meant reading a track every few frames forever, allocating a
/// wrapper each time, to notice a change that happens a handful of times per fight. This costs
/// nothing between animations and fires exactly when something changes — the same signal the game's
/// own CreatureAnimator listens to.
/// </summary>
internal static class SquadAnimationFollower
{
    private static Node? _bound;
    private static NCreature? _source;
    private static readonly List<PartySlot> _followers = new();

    /// <summary>
    /// Subscribes to the real character's sprite. Returns false when it has no spine body, in which
    /// case there is nothing to follow and the trigger hook remains the only mechanism.
    /// </summary>
    public static bool Attach(NCreature source, IEnumerable<PartySlot> followers)
    {
        Detach();

        try
        {
            if (source.Visuals?.SpineBody is not { } body) return false;

            _source = source;
            _followers.AddRange(followers);
            _bound = source.Visuals;

            body.ConnectAnimationStarted(Callable.From<GodotObject, GodotObject, GodotObject>(OnAnimationStarted));
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] could not follow the character's animations: {ex.Message}");
            Detach();
            return false;
        }
    }

    /// <summary>Forgets the current combat's actors. The signal dies with the freed sprite.</summary>
    public static void Detach()
    {
        _bound = null;
        _source = null;
        _followers.Clear();
    }

    private static void OnAnimationStarted(GodotObject _, GodotObject __, GodotObject ___)
    {
        try
        {
            if (_source == null || !GodotObject.IsInstanceValid(_source)) { Detach(); return; }
            if (_bound == null || !GodotObject.IsInstanceValid(_bound)) { Detach(); return; }

            string playing = CurrentAnimation(_source.Visuals);
            if (playing.Length == 0) return;

            // "_loop" is the game's own naming for its looping states (idle_loop, relaxed_loop);
            // everything else is a one-shot.
            bool loop = playing.EndsWith("_loop", StringComparison.Ordinal);

            foreach (PartySlot follower in _followers)
            {
                if (!GodotObject.IsInstanceValid(follower.Visuals)) continue;
                // Already there via the trigger hook on the normal path — do not restart it, that
                // would cut the animation back to frame zero.
                if (CurrentAnimation(follower.Visuals) == playing) continue;
                SpineCompat.Play(follower.Visuals.SpineAnimation, playing, loop);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] animation follow failed: {ex.Message}");
            Detach();
        }
    }

    private static string CurrentAnimation(NCreatureVisuals visuals)
    {
        try
        {
            if (!visuals.HasSpineAnimation) return "";
            // Value-returning accessor rather than GetCurrentTrack()?.GetAnimation()?.GetName():
            // the chain leaves two wrappers for the finalizer, which v0.110.0 made unsafe.
            return visuals.SpineAnimation.GetCurrentAnimationName() ?? "";
        }
        catch { return ""; }
    }
}
