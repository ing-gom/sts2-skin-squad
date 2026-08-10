using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;

namespace Sts2SkinSquad;

/// <summary>
/// The two things about the MegaSpine bindings that differ between game branches, in one place.
///
/// The mod ships a single DLL to players on public (v0.107.1) and public-beta (v0.110.1) alike, so
/// nothing here may bind to a shape that exists on only one of them.
///
/// ★1. SIGNATURES. v0.110.0 turned <c>SetAnimation</c> and <c>AddAnimation</c> — on both
/// <see cref="MegaAnimationState"/> and <see cref="SpineAnimationAccess"/> — from returning
/// <c>MegaTrackEntry?</c> into returning <c>void</c>, moving the tracked variant to a separate
/// <c>AddAnimationTracked</c>. A .NET method reference carries its return type, so a direct call
/// compiles against exactly one branch and throws <see cref="MissingMethodException"/> the moment
/// the containing method is JITted on the other. <see cref="Play"/> late-binds it once into a
/// delegate — the animation path runs per attack, cast and hit of every squad member, so a
/// reflective invoke per call would sit in the wrong place.
///
/// ★2. LIFETIME. v0.110.0 also made <c>MegaSpineBinding</c> implement <see cref="IDisposable"/>,
/// to keep a transient wrapper's signal disconnect on the calling thread instead of the .NET
/// finalizer (the game's own PRG-6985 note).
///
/// ★★That is NOT a licence to dispose every wrapper. <c>Dispose</c> is
/// <c>BoundObject.Dispose()</c> — it frees the wrapped Godot object itself. A
/// <see cref="MegaSprite"/> built over a SpineSprite node wraps the node, and an animation state
/// or skeleton read off it is the sprite's own, shared: disposing any of those destroys live
/// scene objects. Measured on v0.110.1, doing so took out every double, every preview and the
/// skeleton swap with "Cannot access a disposed object". Only entries the bindings document as
/// the caller's — the track entry from <c>GetCurrent</c>/<c>GetCurrentTrack</c> — are released
/// here; everything else is left exactly as v0.107.1 treated it. Better still is not to create
/// the transient at all, which is why the animation name is read through the value-returning
/// accessor.
///
/// v0.107.1 has no <c>Dispose</c> at all, which is why <see cref="Release"/> is a runtime type test
/// and not a <c>using</c> block: <c>using</c> would emit a cast to <see cref="IDisposable"/> that
/// fails on the older build.
/// </summary>
internal static class SpineCompat
{
    /// <summary>Signature on public-beta v0.110.0 and later.</summary>
    private static readonly Action<MegaAnimationState, string, bool, int>? SetAnimationVoid;

    /// <summary>Signature on public v0.107.1 and earlier.</summary>
    private static readonly Func<MegaAnimationState, string, bool, int, MegaTrackEntry?>? SetAnimationTracked;

    static SpineCompat()
    {
        MethodInfo? method = typeof(MegaAnimationState).GetMethod(
            "SetAnimation",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(string), typeof(bool), typeof(int) },
            modifiers: null);

        if (method == null)
        {
            MainFile.Logger.Warn(
                $"[{MainFile.ModId}] MegaAnimationState.SetAnimation not found on this game build; " +
                "squad members will stand in their setup pose.");
            return;
        }

        try
        {
            if (method.ReturnType == typeof(void))
            {
                SetAnimationVoid = (Action<MegaAnimationState, string, bool, int>)Delegate.CreateDelegate(
                    typeof(Action<MegaAnimationState, string, bool, int>), method);
            }
            else
            {
                SetAnimationTracked = (Func<MegaAnimationState, string, bool, int, MegaTrackEntry?>)Delegate.CreateDelegate(
                    typeof(Func<MegaAnimationState, string, bool, int, MegaTrackEntry?>), method);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn(
                $"[{MainFile.ModId}] could not bind MegaAnimationState.SetAnimation returning " +
                $"{method.ReturnType.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Releases a track entry the bindings handed over on the calling thread. No-op on builds
    /// without <see cref="IDisposable"/>, and on null.
    ///
    /// Only ever call this on something the bindings document as the caller's to dispose — see the
    /// type remarks for what happens otherwise.
    /// </summary>
    public static void Release(object? wrapper)
    {
        if (wrapper is not IDisposable disposable) return;

        try
        {
            disposable.Dispose();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] releasing a Spine wrapper threw: {ex.Message}");
        }
    }

    /// <summary>Starts <paramref name="animationName"/> on an animation state the caller owns.</summary>
    public static void Play(MegaAnimationState? state, string animationName, bool loop = true, int trackId = 0)
    {
        if (state == null) return;

        if (SetAnimationTracked != null)
        {
            // The old branch hands back a track entry nobody asked for; it is a wrapper like any
            // other, so it does not get to escape into the finalizer either.
            Release(SetAnimationTracked(state, animationName, loop, trackId));
            return;
        }

        SetAnimationVoid?.Invoke(state, animationName, loop, trackId);
    }

    /// <summary>
    /// Same, through the null-safe struct the creature nodes hand out.
    ///
    /// <c>SpineAnimationAccess.SetAnimation</c> changed return type on the same branch and would
    /// need its own late binding, but its body is exactly
    /// <c>_sprite?.GetAnimationState().SetAnimation(...)</c> and <c>GetAnimationState</c> is
    /// unchanged — so this is the same call with one binding instead of two. The state it returns
    /// is a fresh wrapper and belongs to us.
    /// </summary>
    public static void Play(SpineAnimationAccess access, string animationName, bool loop = true, int trackId = 0)
        => Play(access.GetAnimationState(), animationName, loop, trackId);

    /// <summary>Same, on a bare SpineSprite node — the campfire, shop and preview rigs.</summary>
    public static void PlayOnSprite(Node2D spineSprite, string animationName, bool loop = true, int trackId = 0)
        => Play(new MegaSprite(spineSprite).GetAnimationState(), animationName, loop, trackId);

    /// <summary>
    /// Starts a looping animation at a random point in its own cycle, so a row of identical rigs
    /// does not breathe in lockstep.
    ///
    /// Reading the track back is the one thing that genuinely needs a <see cref="MegaTrackEntry"/>,
    /// so it happens here, where the entry can be released before returning.
    /// </summary>
    public static void PlayDesynced(Node2D spineSprite, string animationName, float phase)
    {
        MegaAnimationState state = new MegaSprite(spineSprite).GetAnimationState();
        Play(state, animationName, loop: true);

        MegaTrackEntry? track = state.GetCurrent(0);
        try
        {
            if (track != null) track.SetTrackTime(track.GetAnimationEnd() * phase);
        }
        finally
        {
            Release(track);
        }
    }

    /// <summary>
    /// Name of the animation playing on a bare SpineSprite, or null.
    ///
    /// Goes through <c>GetCurrentAnimationName</c> rather than
    /// <c>GetCurrent()?.GetAnimation()?.GetName()</c>: the value-returning accessor is what the
    /// bindings added for exactly this, and the chained form builds two more wrappers per call.
    /// </summary>
    public static string? CurrentAnimationName(Node2D spineSprite)
        => new MegaSprite(spineSprite).GetAnimationState().GetCurrentAnimationName(0);
}
