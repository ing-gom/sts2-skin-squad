using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;

namespace Sts2SkinSquad;

/// <summary>
/// Replaces the skeleton data on one <see cref="NCreatureVisuals"/> instance, leaving the node
/// itself in place so the scene's markers, VFX nodes and scale keep working.
///
/// Per-INSTANCE is the whole point. The usual way to change a character's look is mounting a .pck
/// that overrides <c>res://animations/characters/...</c>, which is global by construction: two skins
/// for one character claim the same path and the last mount wins. Swapping the loaded resource on a
/// single SpineSprite has no such conflict, so three doubles of the same character can wear three
/// different skins at once.
///
/// The spine-godot GDExtension is reached through ClassDB rather than a typed reference: the game
/// bundles it, but nothing in sts2.dll exposes it to mods.
/// </summary>
internal static class SpineSwap
{
    private const string IdleAnimation = "idle_loop";

    private static Variant Inst(string cls) => ClassDB.Instantiate(cls);

    /// <summary>
    /// Loads an atlas + skeleton pair from disk and binds it to this actor's SpineSprite.
    /// Returns false (and logs) if anything in the chain fails; the caller keeps the stock body.
    /// </summary>
    public static bool Apply(NCreatureVisuals visuals, string atlasPath, string skeletonPath)
    {
        if (visuals.GetNodeOrNull<Node2D>("%Visuals") is not { } body)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] '{visuals.Name}' has no %Visuals node; cannot apply a skin.");
            return false;
        }
        return ApplyToSprite(body, atlasPath, skeletonPath, IdleAnimation);
    }

    /// <summary>
    /// Swaps the skeleton on a bare SpineSprite. Used for the campfire and shop rigs, whose actors
    /// are not <see cref="NCreatureVisuals"/> and whose animation names differ per act, hence
    /// <paramref name="restartAnimation"/> being optional: pass null to leave whatever the caller
    /// already started playing.
    /// </summary>
    public static bool ApplyToSprite(Node2D body, string atlasPath, string skeletonPath, string? restartAnimation)
    {
        try
        {
            if (body.GetClass() != "SpineSprite") return false;

            Resource? data = LoadData(atlasPath, skeletonPath);
            if (data == null) return false;

            // Measure the stock body first, in the pose we will compare against.
            if (restartAnimation != null) SetAnimation(body, restartAnimation);
            float stockHeight = MeasureHeight(body);
            string? playing = restartAnimation ?? CurrentAnimation(body);

            body.Call("set_skeleton_data_res", data);

            // Binding new data resets the animation state, so playback has to be restarted or the
            // actor freezes in its setup pose.
            if (playing != null) SetAnimation(body, playing);

            NormalizeScale(body, stockHeight);
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] skeleton swap threw: {ex.Message}");
            return false;
        }
    }

    private static void SetAnimation(Node2D spineSprite, string name)
    {
        try { new MegaSprite(spineSprite).GetAnimationState().SetAnimation(name); }
        catch (Exception ex) { MainFile.Logger.Warn($"[{MainFile.ModId}] could not play '{name}': {ex.Message}"); }
    }

    private static string? CurrentAnimation(Node2D spineSprite)
    {
        try { return new MegaSprite(spineSprite).GetAnimationState().GetCurrent(0)?.GetAnimation()?.GetName(); }
        catch { return null; }
    }

    /// <summary>
    /// Rescales the swapped body so it stands the same height as the character it replaces.
    ///
    /// This is needed because we load the skeleton's raw bytes instead of mounting the skin's .pck.
    /// Mounting would route the assets through Godot's importer, which applies the author's import
    /// settings; a raw load gets none of that, so a skeleton authored at a different pixel scale
    /// renders visibly too small or too large next to the stock characters. Measured in-game: a
    /// Necrobinder skin came out noticeably shorter than the vanilla body at identical node scale.
    /// </summary>
    private static void NormalizeScale(Node2D body, float stockHeight)
    {
        string who = body.GetParent()?.Name.ToString() ?? body.Name.ToString();
        float skinHeight = MeasureHeight(body);
        if (stockHeight <= 1f || skinHeight <= 1f)
        {
            MainFile.Logger.Info($"[{MainFile.ModId}] '{who}': no usable bounds (stock={stockHeight:F1}, skin={skinHeight:F1}); leaving scale as authored.");
            return;
        }

        float ratio = stockHeight / skinHeight;

        // A ratio this far out means the measurement is wrong, not the art. Correcting on it would
        // turn a slightly-off double into an invisible speck or a screen-filling one.
        if (ratio < 0.1f || ratio > 10f)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] '{who}': implausible scale ratio {ratio:F2}; leaving scale as authored.");
            return;
        }

        body.Scale *= ratio;
        MainFile.Logger.Info($"[{MainFile.ModId}] '{who}': normalized skin scale x{ratio:F3} (stock {stockHeight:F0} -> skin {skinHeight:F0}).");
    }

    /// <summary>
    /// Builds a standalone, animating SpineSprite from an extracted atlas + skeleton pair — the
    /// picker's live preview. Null if the pair cannot be loaded.
    ///
    /// Skeleton data is cached per file pair: the picker rebuilds a preview on every hover, and
    /// re-reading an atlas page off disk each time would hitch the UI.
    /// </summary>
    public static Node2D? CreateSprite(string atlasPath, string skeletonPath, string animation)
    {
        try
        {
            Resource? data = LoadDataCached(atlasPath, skeletonPath);
            if (data == null) return null;

            var sprite = Inst("SpineSprite").As<Node2D>();
            sprite.Call("set_skeleton_data_res", data);
            PlayOn(sprite, animation);
            return sprite;

        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] preview sprite failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Starts an animation on a SpineSprite, looping, and forces the pose to be applied right away.
    ///
    /// ★The apply matters. Setting an animation only queues it; until the state machine ticks, the
    /// skeleton is still in its setup pose and <c>GetBounds()</c> reports nothing useful — which made
    /// the preview panel's scale normalisation silently give up and render each skin at its authored
    /// size. Update(0)+Apply is exactly what the game's own CreatureAnimator constructor does before
    /// it reads a track.
    /// </summary>
    public static void PlayOn(Node2D spineSprite, string animation)
    {
        try
        {
            var mega = new MegaSprite(spineSprite);

            // ★Not every skeleton names its idle "idle_loop". A skin that does not simply never
            // started anything, so it sat in its setup pose — which for some rigs is a limp
            // T-pose-ish sprawl and for others is barely on screen at all. Fall back to whatever
            // idle it does have, then to its first animation.
            if (!mega.HasAnimation(animation))
            {
                string? substitute = PickFallbackAnimation(spineSprite, animation);
                if (substitute == null)
                {
                    MainFile.Logger.Info($"[{MainFile.ModId}] '{spineSprite.Name}' has no animations at all; leaving the setup pose.");
                    return;
                }
                MainFile.Logger.Info($"[{MainFile.ModId}] '{animation}' missing; playing '{substitute}' instead.");
                animation = substitute;
            }

            MegaAnimationState state = mega.GetAnimationState();
            state.SetAnimation(animation, true);
            state.Update(0f);
            MegaSkeleton? skeleton = mega.GetSkeleton();
            if (skeleton != null) state.Apply(skeleton);
        }
        catch { /* skeleton lacks that animation; it just stands in its setup pose */ }
    }

    /// <summary>Closest thing to <paramref name="wanted"/> this skeleton actually has, or null.</summary>
    private static string? PickFallbackAnimation(Node2D spineSprite, string wanted)
    {
        List<string> names = AnimationNames(spineSprite);
        if (names.Count == 0) return null;
        return names.FirstOrDefault(n => n.Contains("idle", StringComparison.OrdinalIgnoreCase))
               ?? names.FirstOrDefault(n => !string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase))
               ?? names[0];
    }

    /// <summary>Animation names on a SpineSprite's skeleton. Empty when unavailable.</summary>
    public static List<string> AnimationNames(Node2D spineSprite)
    {
        var found = new List<string>();
        try
        {
            GodotObject? skeleton = spineSprite.Call("get_skeleton").AsGodotObject();
            GodotObject? data = skeleton?.Call("get_data").AsGodotObject();
            if (data == null) return found;

            foreach (Variant entry in data.Call("get_animations").AsGodotArray())
            {
                GodotObject? anim = entry.AsGodotObject();
                if (anim == null) continue;
                string name = anim.HasMethod("get_animation_name") ? anim.Call("get_animation_name").ToString()
                            : anim.HasMethod("get_name") ? anim.Call("get_name").ToString()
                            : "";
                if (name.Length > 0) found.Add(name);
            }
        }
        catch { /* fall through with whatever we collected */ }
        return found;
    }

    /// <summary>Skeleton-local bounds of the current pose, or a zero rect when unavailable.</summary>
    public static Rect2 MeasureBounds(Node2D spineSprite)
    {
        try { return new MegaSprite(spineSprite).GetSkeleton()?.GetBounds() ?? new Rect2(); }
        catch { return new Rect2(); }
    }

    private static readonly System.Collections.Generic.Dictionary<string, Resource?> _dataCache = new();

    private static Resource? LoadDataCached(string atlasPath, string skeletonPath)
    {
        string key = atlasPath + "|" + skeletonPath;
        if (_dataCache.TryGetValue(key, out Resource? cached) && cached != null && GodotObject.IsInstanceValid(cached))
            return cached;

        Resource? data = LoadData(atlasPath, skeletonPath);
        _dataCache[key] = data;
        return data;
    }

    /// <summary>Skeleton-local bounds height of the current pose, or 0 when unavailable.</summary>
    private static float MeasureHeight(Node2D spineSprite)
    {
        try
        {
            GodotObject? skeleton = spineSprite.Call("get_skeleton").AsGodotObject();
            if (skeleton == null) return 0f;
            return skeleton.Call("get_bounds").AsRect2().Size.Y;
        }
        catch { return 0f; }
    }

    private static Resource? LoadData(string atlasPath, string skeletonPath)
    {
        var atlas = Inst("SpineAtlasResource").As<Resource>();
        var atlasErr = (int)atlas.Call("load_from_atlas_file", atlasPath);
        if (atlasErr != 0)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] atlas load err={atlasErr}: {atlasPath}");
            return null;
        }

        var file = Inst("SpineSkeletonFileResource").As<Resource>();
        var skelErr = (int)file.Call("load_from_file", skeletonPath);
        if (skelErr != 0)
        {
            // err 30 (ERR_INVALID_DATA) with no detail is almost always the extension, not the bytes:
            // spine-godot only accepts .skel / .spskel / .spjson / .spine-json.
            MainFile.Logger.Warn($"[{MainFile.ModId}] skeleton load err={skelErr}: {skeletonPath}");
            return null;
        }

        var data = Inst("SpineSkeletonDataResource").As<Resource>();
        data.Call("set_atlas_res", atlas);
        data.Call("set_skeleton_file_res", file);
        if (!(bool)data.Call("is_skeleton_data_loaded"))
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] skeleton data did not load (atlas/skeleton mismatch): {skeletonPath}");
            return null;
        }
        return data;
    }
}
