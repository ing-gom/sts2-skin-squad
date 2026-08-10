using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Sts2SkinSquad.Localization;

namespace Sts2SkinSquad.Ui;

/// <summary>
/// The picker's live preview: the hovered look, standing in its idle animation.
///
/// A still image was not enough to tell entries apart. A mounted skin replaces the stock character,
/// so several list positions can show near-identical portraits; watching the actual skeleton move is
/// what makes a choice obvious. The art is already extracted for the squad itself, so this is the
/// same data bound to a throwaway sprite.
/// </summary>
internal sealed partial class SkinPreview : Control
{
    private const string IdleAnimation = "idle_loop";

    /// <summary>Fraction of the box height the figure is scaled to fill.</summary>
    private const float FillRatio = 0.82f;

    /// <summary>Where the figure's feet sit, as a fraction of box height.</summary>
    private const float GroundRatio = 0.94f;

    /// <summary>
    /// Hover-to-render delay. Sweeping the pointer down the list would otherwise build and throw
    /// away a skeleton per row, and the first build of a skin also unpacks it from its archive.
    /// </summary>
    private const double DebounceSec = 0.12;

    /// <summary>Retries while the skeleton settles; bounds can read as empty on the first frames.</summary>
    private const int FitAttempts = 6;
    private const double FitRetrySec = 0.05;

    private Node2D? _figure;
    private TextureRect? _still;
    private Label? _caption;
    private string? _shown;
    private string? _pending;
    private SceneTreeTimer? _timer;

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;   // never steal hover from the rows driving it

        // Still portrait behind the animated figure. It carries the panel for entries that have no
        // animatable rig (Self, Random, a skin whose skeleton fails to load) and shows instantly
        // while the live one is still being unpacked.
        _still = new TextureRect
        {
            // ExpandMode first: on the default mode a TextureRect reports the raw image size as its
            // minimum and drags the whole panel wider.
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _still.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_still);

        _caption = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _caption.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _caption.AddThemeColorOverride("font_color", new Color(0.78f, 0.74f, 0.66f));
        AddChild(_caption);

        Resized += () => Reposition();
    }

    /// <summary>Sanity numbers for the harness and for logs: the height the figure ended up at.</summary>
    public float FittedHeight()
    {
        if (_figure == null || !GodotObject.IsInstanceValid(_figure)) return 0f;
        Node2D sprite = FindSpineSprite(_figure) ?? _figure;
        float inner = ReferenceEquals(sprite, _figure) ? 1f : Math.Abs(sprite.Scale.Y);
        return SpineSwap.MeasureBounds(sprite).Size.Y * inner * Math.Abs(_figure.Scale.Y);
    }

    /// <summary>
    /// The height normalisation actually landed on, recorded at fit time. 0 until a figure has been
    /// fitted.
    ///
    /// ★Why this exists alongside <see cref="FittedHeight"/>: that one re-measures the skeleton
    /// live, and the figure is playing its idle, so it reports the CURRENT pose. Measured on
    /// v0.110.1, one look's silhouette swings 1.42x over a single idle cycle — more than the
    /// spread between different looks. Comparing looks by live height therefore says almost
    /// nothing about whether they were normalised, which is the property the panel exists to
    /// guarantee (and the one that regressed in v0.10.1). This value is deterministic per look.
    /// </summary>
    public float NormalizedHeight() => _normalizedHeight;

    private float _normalizedHeight;

    /// <summary>
    /// Empties the panel outright — figure, portrait and caption.
    ///
    /// Distinct from Show(null): with no squad members there is nothing to preview at all, and
    /// leaving the last hovered figure on screen made the panel look like it had frozen.
    /// </summary>
    public void ShowNothing()
    {
        _pending = null;
        _timer = null;                    // cancels any in-flight debounced build
        _shown = null;
        Clear();
        if (_still != null) { _still.Texture = null; _still.Visible = false; }
        if (_caption != null) _caption.Text = "";
    }

    /// <summary>Requests a preview of <paramref name="token"/>. Debounced; repeats are ignored.</summary>
    public void Show(string? token)
    {
        if (token == _shown && _figure != null) return;
        _pending = token;

        _timer = GetTree()?.CreateTimer(DebounceSec);
        if (_timer == null) { Build(token); return; }

        SceneTreeTimer captured = _timer;
        captured.Timeout += () =>
        {
            // Only the most recent request wins: sweeping the list fires several of these.
            if (!ReferenceEquals(captured, _timer)) return;
            if (!GodotObject.IsInstanceValid(this)) return;
            Build(_pending);
        };
    }

    private void Build(string? token)
    {
        try
        {
            Clear();
            _shown = token;
            if (_caption != null)
                _caption.Text = token == Appearance.SelfToken
                    ? Strings.Get("look_self_caption")
                    : Appearance.DisplayName(token);

            Texture2D? still = Appearance.Preview(token);
            if (_still != null) { _still.Texture = still; _still.Visible = still != null; }

            _figure = CreateFigure(token);
            if (_figure == null) return;

            AddChild(_figure);
            MoveChild(_figure, 0);      // behind the still and the caption

            // A sprite-based rig has its own AnimationPlayer and nobody starts it, unlike the Spine
            // path where the sprite is created already playing.
            StartAnimationPlayerIdle(_figure);

            // ★A figure with no animations is not worth showing live: it renders in its setup pose,
            // which for many rigs is a sprawl of detached parts or sits nowhere near the box. That
            // is the "no spine idle means nothing appears" report — the portrait had already been
            // hidden in favour of a figure that could not be seen. Keep the portrait instead, and
            // say why, so the choice is informed rather than puzzling.
            if (!HasAnimations(_figure))
            {
                Clear();
                if (_still != null) _still.Visible = _still.Texture != null;
                if (_caption != null) _caption.Text += "\n(no animations — stands still)";
                return;
            }

            // The live figure supersedes the portrait; keeping both would double the character up.
            if (_still != null) _still.Visible = false;
            Reposition();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] preview for '{token}' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The figure for a token. Skins and vanilla entries come from extracted files as a bare
    /// SpineSprite; a custom character has no extractable archive entry, so its own visuals scene is
    /// instantiated instead.
    /// </summary>
    private static Node2D? CreateFigure(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token == Appearance.SelfToken || token == Appearance.RandomToken)
            return null;

        SkinVariant? variant = SkinCatalog.Find(token);
        if (variant != null)
        {
            RigFiles? files = SkinCatalog.TryMaterialize(variant, SkinRig.Combat);
            return files == null ? null : SpineSwap.CreateSprite(files.AtlasFile, files.SkelFile, IdleAnimation);
        }

        CharacterModel? model = Appearance.FindCharacterModel(token);
        NCreatureVisuals? visuals = model?.CreateVisuals();
        if (visuals == null) return null;
        return visuals;
    }

    /// <summary>
    /// Centres the figure in the box and scales it to a consistent height.
    ///
    /// Needed because these skeletons are authored at wildly different pixel scales — the same
    /// reason the squad itself needs scale normalisation. Without it one skin fills the panel and
    /// the next is a speck.
    /// </summary>
    private void Reposition(int attemptsLeft = FitAttempts)
    {
        if (_figure == null || !GodotObject.IsInstanceValid(_figure)) return;

        Node2D sprite = FindSpineSprite(_figure) ?? _figure;

        // Reset before measuring: the fit is computed from the figure's NATURAL height, so a scale
        // left over from a previous attempt would compound.
        _figure.Scale = Vector2.One;
        Rect2 bounds = SpineSwap.MeasureBounds(sprite);

        // An NCreatureVisuals scene carries its own inner scale; a bare sprite does not.
        float inner = ReferenceEquals(sprite, _figure) ? 1f : Math.Abs(sprite.Scale.Y);
        float naturalHeight = bounds.Size.Y * inner;

        // A sprite-based character has no skeleton to measure. Its scene still carries the Bounds
        // marker the game itself sizes and spaces creatures by, which is already in the figure's own
        // space — so use that rather than giving up and falling back to a portrait.
        if (naturalHeight <= 1f && _figure is NCreatureVisuals visuals && visuals.Bounds != null
            && visuals.Bounds.Size.Y > 1f)
        {
            bounds = new Rect2(visuals.Bounds.Position, visuals.Bounds.Size);
            inner = 1f;
            naturalHeight = bounds.Size.Y;
        }

        if (naturalHeight <= 1f)
        {
            // ★Bounds are not ready yet. Never leave the figure at scale 1 and call it done — these
            // skeletons are authored at wildly different pixel scales, so an unnormalised figure is
            // exactly the "every preview is a different size" bug. Retry on later frames, and if it
            // never resolves, drop back to the still portrait rather than show a wrong-sized body.
            if (attemptsLeft > 0)
            {
                SceneTreeTimer? t = GetTree()?.CreateTimer(FitRetrySec);
                if (t != null) { t.Timeout += () => { if (GodotObject.IsInstanceValid(this)) Reposition(attemptsLeft - 1); }; return; }
            }

            MainFile.Logger.Info($"[{MainFile.ModId}] preview '{_shown}': no skeleton bounds; showing the portrait instead.");
            Clear();
            if (_still != null) _still.Visible = _still.Texture != null;
            return;
        }

        float fit = Size.Y * FillRatio / naturalHeight;
        if (fit <= 0.001f || fit >= 1000f) return;
        _figure.Scale = new Vector2(fit, fit);
        _normalizedHeight = naturalHeight * fit;

        // Vertical from the measured rect, horizontal from the rig's own origin.
        //
        // Vertically the rect is what matters: rigs are authored around their centre as often as
        // their feet, and assuming feet put some of them outside the clipped box entirely.
        //
        // Horizontally the rect is the wrong guide. It spans everything attached to the skeleton, so
        // a long weapon or a trailing effect drags the measured centre away from the body and the
        // character lands visibly off to one side (reported on the raye skin). The root is the
        // reliable anchor — it is what the game itself positions characters by in combat, which is
        // why they line up there.
        float bottomY = bounds.End.Y * inner * fit;
        _figure.Position = new Vector2(Size.X * 0.5f, Size.Y * GroundRatio - bottomY);
    }

    /// <summary>
    /// Whether this figure has anything to play — a Spine skeleton with clips, or the plain
    /// AnimationPlayer that sprite-based character mods use.
    /// </summary>
    private static bool HasAnimations(Node figure)
    {
        Node2D? sprite = FindSpineSprite(figure);
        if (sprite != null && SpineSwap.AnimationNames(sprite).Count > 0) return true;
        return FindAnimationPlayer(figure) != null;
    }

    /// <summary>Starts a sprite rig's idle so the preview is animated, not a frozen first frame.</summary>
    private static void StartAnimationPlayerIdle(Node figure)
    {
        AnimationPlayer? player = FindAnimationPlayer(figure);
        if (player == null) return;
        try
        {
            string[] clips = player.GetAnimationList();
            string? idle = clips.FirstOrDefault(c => c.Contains("idle", StringComparison.OrdinalIgnoreCase))
                           ?? clips.FirstOrDefault(c => !string.Equals(c, "RESET", StringComparison.OrdinalIgnoreCase));
            if (idle != null) player.Play(idle);
        }
        catch { /* a rig we cannot drive just stands still */ }
    }

    private static AnimationPlayer? FindAnimationPlayer(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is AnimationPlayer player) return player;
            if (FindAnimationPlayer(child) is { } nested) return nested;
        }
        return null;
    }

    private static Node2D? FindSpineSprite(Node root)
    {
        if (root is Node2D n && n.GetClass() == "SpineSprite") return n;
        foreach (Node c in root.GetChildren())
        {
            Node2D? found = FindSpineSprite(c);
            if (found != null) return found;
        }
        return null;
    }

    private void Clear()
    {
        if (_figure != null && GodotObject.IsInstanceValid(_figure))
        {
            RemoveChild(_figure);
            _figure.QueueFree();
        }
        _figure = null;
        // Cleared with the figure, so a look that never fits reads 0 instead of inheriting the
        // previous look's number.
        _normalizedHeight = 0f;
    }
}
