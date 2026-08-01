using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using Sts2SkinSquad.Localization;

namespace Sts2SkinSquad.Ui;

/// <summary>
/// Puts a "Squad" button beside Embark on the character-select screen, opening
/// <see cref="SquadModal"/>.
///
/// A plain Godot <see cref="Button"/>, not a copy of a native one. Duplicating Embark was the
/// obvious move, but <c>NConfirmButton</c> is an image button with no label at all, so a copy would
/// read as a second Embark. Godot themes propagate down the tree, so a plain Button added under
/// this screen still picks up the game's button styling.
/// </summary>
internal static class SquadButton
{
    private const string NodeName = "SkinSquadButton";
    private const float Width = 160f;
    private const float Height = 56f;

    private const float GapUnderPanel = 8f;

    // Matches Skin Manager's own Save / Discard buttons so the row stays visually uniform.
    private const float PanelButtonWidth = 120f;
    private const float PanelButtonHeight = 36f;

    // Skin Manager attaches its panel a few frames after we attach ours; poll rather than race.
    private const int TrackAttempts = 12;
    private const double TrackIntervalSec = 0.25;

    /// <summary>Skin Manager parents its panel at this ZIndex; ours must clear it.</summary>
    private const int SkinManagerZ = 1000;
    private const int OverlayZ = 1100;

    // Fallback placement, measured against the settled screen: Embark's checkmark occupies roughly
    // the rightmost 160 px and sits about 295 px up from the bottom, so these clear it.
    private const float RightMargin = 230f;
    private const float BottomMargin = 267f;

    public static void Attach(NCharacterSelectScreen screen)
    {
        try
        {
            if (screen == null || !GodotObject.IsInstanceValid(screen)) return;
            if (screen.HasNode(NodeName)) return;

            var embark = screen.GetNodeOrNull<NConfirmButton>("ConfirmButton");
            if (embark == null)
            {
                MainFile.Logger.Warn($"[{MainFile.ModId}] ConfirmButton not found; no squad button added.");
                return;
            }

            var button = new SquadDragButton
            {
                Name = NodeName,
                Text = Strings.Get("btn_squad"),
                CustomMinimumSize = new Vector2(Width, Height),
                // Above Skin Manager's own controls, which sit at ZIndex 1000 and would otherwise
                // paint over a plain sibling.
                ZIndex = OverlayZ,
            };
            embark.AddSibling(button, false);

            PlaceBottomRight(button);
            button.ApplyDelta(SquadConfig.ButtonDelta);      // restore where the user left it
            button.Clicked += SquadModal.Open;
            button.Moved += delta => { SquadConfig.ButtonDelta = delta; SquadConfig.Save(); };
            MainFile.Logger.Info($"[{MainFile.ModId}] character-select 'Squad' button added (draggable).");

            // Skin Manager builds its panel on a later frame than our own attach — measured: at
            // this point the screen holds none of its nodes. Harmony gives no ordering guarantee
            // between mods, so instead of racing, watch briefly and move next to the panel the
            // moment it appears. If it never does (mod not installed), the corner placement stands.
            PollForSkinManager(screen, button, TrackAttempts);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] squad button add failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-checks for Skin Manager's panel a few times, moving the button next to it on the first
    /// sighting. Stops as soon as it lands, or when the attempts run out.
    /// </summary>
    private static void PollForSkinManager(Control screen, Control button, int attemptsLeft)
    {
        if (attemptsLeft <= 0) return;
        if (!GodotObject.IsInstanceValid(screen) || !GodotObject.IsInstanceValid(button)) return;

        SceneTree? tree = screen.GetTree();
        if (tree == null) return;

        tree.CreateTimer(TrackIntervalSec).Timeout += () =>
        {
            try
            {
                if (!GodotObject.IsInstanceValid(screen) || !GodotObject.IsInstanceValid(button)) return;

                Control? panel = FindSkinManagerPanel(screen, quiet: attemptsLeft > 1);
                if (panel != null)
                {
                    if (!TryMountInPanel(button, panel)) PlaceUnder(button, panel);
                    return;
                }
                PollForSkinManager(screen, button, attemptsLeft - 1);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[{MainFile.ModId}] squad button tracking failed: {ex.Message}");
            }
        };
    }

    /// <summary>
    /// Finds Skin Manager's character-select panel, so the Squad button can sit with it rather than
    /// in an unrelated corner — the two mods are configured together.
    ///
    /// Located by shape, not by name: Skin Manager adds its panel without naming the node, so Godot
    /// auto-names it and there is nothing stable to look up. What is distinctive is a VBoxContainer
    /// parented straight to the screen at ZIndex 1000. Returns null when Skin Manager is not
    /// installed, which is a normal case, not an error.
    /// </summary>
    private static Control? FindSkinManagerPanel(Control screen, bool quiet = false)
    {
        try
        {
            // ZIndex EXACTLY 1000, not ">= 1000". Skin Manager's hover preview is also a
            // VBoxContainer parented to the screen, at ZIndex 3100 and a fixed 240x280 — with a
            // ">=" filter it matched and, being the larger rect, won the sort, so the button was
            // placed under a panel that is invisible most of the time.
            var candidates = screen.GetChildren()
                .OfType<VBoxContainer>()
                .Where(v => v.ZIndex == SkinManagerZ && v.GetChildCount() > 0)
                .OrderByDescending(v => v.Size.X)
                .ToList();

            if (candidates.Count > 0) return candidates[0];

            // Only report on the LAST attempt: on the earlier ones the panel is simply not built
            // yet, and logging every poll would bury the one line that matters. A layout change in
            // the other mod still produces a diagnosable dump rather than a silent fallback.
            if (!quiet)
            {
                string seen = string.Join(", ", screen.GetChildren().OfType<Control>()
                    .Select(c => $"{c.GetType().Name}(z={c.ZIndex})"));
                MainFile.Logger.Info($"[{MainFile.ModId}] Skin Manager panel not found; leaving the button in the corner. Screen children: [{seen}]");
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Moves the button INTO Skin Manager's own menu bar, beside its Save / Discard buttons, so the
    /// two mods read as one panel instead of two competing widgets — and so the button follows when
    /// the user drags that panel.
    ///
    /// This is a soft integration, not a dependency: nothing is required of the other mod at load
    /// time, and every path here degrades to the standalone placement.
    /// </summary>
    private static bool TryMountInPanel(Control button, Control panel)
    {
        try
        {
            // The menu bar is the panel's first child: an HBoxContainer holding the drag grip, the
            // expand toggle, and Save / Discard.
            if (panel.GetChildOrNull<HBoxContainer>(0) is not { } topRow) return false;

            // Inside the panel the button is a row item: the container owns its position, and it
            // must move only when the user drags that panel. Its own drag handler would fight the
            // layout and eat clicks.
            if (button is SquadDragButton drag)
            {
                drag.DragEnabled = false;
                drag.RefreshTooltip();
            }

            button.GetParent()?.RemoveChild(button);

            // A container owns its children's layout, so anchors and offsets must be handed back
            // or the button keeps the corner geometry it was created with. Any saved drag offset
            // goes with them: inside the panel the button moves when the panel does.
            button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
            button.CustomMinimumSize = new Vector2(PanelButtonWidth, PanelButtonHeight);
            button.ZIndex = 0;                       // inherits the panel's ZIndex now
            topRow.AddChild(button);

            MainFile.Logger.Info($"[{MainFile.ModId}] 'Squad' button mounted inside the Skin Manager panel.");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] could not mount inside the Skin Manager panel ({ex.Message}); placing it alongside instead.");
            return false;
        }
    }

    private static void PlaceUnder(Control button, Control panel)
    {
        // Use the panel's RENDERED rect rather than copying its anchors and offsets. Skin Manager's
        // panel is draggable and stores that drag as an offset delta on top of its anchors, so
        // reconstructing its position from those fields put the button in a different corner
        // entirely. By this point the screen has settled, so the global rect is the honest answer.
        Rect2 rect = panel.GetGlobalRect();

        button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        button.CustomMinimumSize = new Vector2(Width, Height);
        button.Size = new Vector2(Width, Height);
        button.GlobalPosition = new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y + GapUnderPanel);

        MainFile.Logger.Info($"[{MainFile.ModId}] placed under panel: panel={rect}, button={button.GetGlobalRect()}.");
    }

    /// <summary>
    /// Fallback when Skin Manager is absent: the screen's bottom-right, clear of Embark.
    ///
    /// ★Anchors, not a measurement of Embark. Embark slides in, so reading its rect while the screen
    /// is still animating reported it at x=1940 on a 1920-wide screen and parked the button half off
    /// the edge. Anchors are stable from the first frame and scale with the viewport.
    /// </summary>
    private static void PlaceBottomRight(Control button)
    {
        button.AnchorLeft = button.AnchorRight = 1f;
        button.AnchorTop = button.AnchorBottom = 1f;
        button.OffsetRight = -RightMargin;
        button.OffsetLeft = -RightMargin - Width;
        button.OffsetBottom = -BottomMargin;
        button.OffsetTop = -BottomMargin - Height;
    }
}
