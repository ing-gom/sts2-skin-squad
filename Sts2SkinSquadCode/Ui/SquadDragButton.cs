using System;
using Godot;
using Sts2SkinSquad.Localization;

namespace Sts2SkinSquad.Ui;

/// <summary>
/// The standalone Squad button: clickable, and draggable so it can be moved clear of whatever else
/// a user has parked in that corner.
///
/// Only used when Skin Manager is absent. With it installed the button lives inside that mod's menu
/// bar and moves with it, so it needs no dragging of its own.
/// </summary>
internal sealed partial class SquadDragButton : Button
{
    /// <summary>Pointer travel before a press becomes a drag. Below this it is still a click.</summary>
    private const float DragThresholdPx = 6f;

    /// <summary>Kept on screen even if a saved position came from a different resolution.</summary>
    private const float ScreenMargin = 4f;

    private bool _pressing, _dragged;
    private Vector2 _grabScreen, _startOffsetTopLeft;

    /// <summary>Raised on a real click — not after a drag.</summary>
    public event Action? Clicked;

    /// <summary>Raised when a drag finishes, with the accumulated delta for persisting.</summary>
    public event Action<Vector2>? Moved;

    /// <summary>Screen-space delta applied on top of whatever anchors the button was given.</summary>
    public Vector2 Delta { get; private set; }

    /// <summary>
    /// Whether the button can be dragged on its own.
    ///
    /// Turned off once it is mounted inside Skin Manager's panel: there it is a row item whose
    /// position the container owns, and it should move only when that panel is dragged. Leaving the
    /// handler on would have it fight the container's layout and swallow clicks.
    /// </summary>
    public bool DragEnabled { get; set; } = true;

    public override void _Ready()
    {
        Pressed += () => { if (!_dragged) Clicked?.Invoke(); };
        MouseDefaultCursorShape = CursorShape.PointingHand;
        RefreshTooltip();
    }

    /// <summary>Keeps the hint honest about whether this instance can actually be dragged.</summary>
    public void RefreshTooltip()
        => TooltipText = Strings.Get(DragEnabled ? "tip_drag" : "tip_click");

    /// <summary>
    /// Applies a stored delta and pulls the button back on screen. Re-clamping here matters because
    /// a position saved at one resolution can land off-screen at another.
    /// </summary>
    public void ApplyDelta(Vector2 delta)
    {
        Delta = Vector2.Zero;
        Shift(delta);
        ClampToViewport();
    }

    // ★A single screen-space delta added to BOTH edges, rather than rewriting the layout.
    // A right- or bottom-anchored control has negative offsets, but adding the same dx to both
    // edges moves it by exactly dx either way — so this works whatever anchors the caller chose.
    private void Shift(Vector2 delta)
    {
        OffsetLeft += delta.X;
        OffsetRight += delta.X;
        OffsetTop += delta.Y;
        OffsetBottom += delta.Y;
        Delta += delta;
    }

    private void ClampToViewport()
    {
        Rect2 view = GetViewportRect();
        Rect2 me = GetGlobalRect();

        float dx = 0f, dy = 0f;
        if (me.Position.X < ScreenMargin) dx = ScreenMargin - me.Position.X;
        else if (me.End.X > view.Size.X - ScreenMargin) dx = view.Size.X - ScreenMargin - me.End.X;
        if (me.Position.Y < ScreenMargin) dy = ScreenMargin - me.Position.Y;
        else if (me.End.Y > view.Size.Y - ScreenMargin) dy = view.Size.Y - ScreenMargin - me.End.Y;

        if (dx != 0f || dy != 0f) Shift(new Vector2(dx, dy));
    }

    // Press and release are picked up here because _GuiInput only fires while the cursor is inside
    // the control — which is exactly right for starting a drag, and useless for continuing one.
    public override void _GuiInput(InputEvent @event)
    {
        if (DragEnabled && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
        {
            _pressing = true;
            _dragged = false;
            _grabScreen = mb.GlobalPosition;
            _startOffsetTopLeft = new Vector2(OffsetLeft, OffsetTop);
        }
        base._GuiInput(@event);
    }

    // ★Motion is handled in _Input, not _GuiInput: _GuiInput stops firing the moment the cursor
    // leaves the control, so a quick drag slides out from under the button and it stops following.
    // _Input runs ahead of GUI routing, so the drag keeps up and the click can be swallowed.
    public override void _Input(InputEvent @event)
    {
        if (!DragEnabled || !_pressing) return;

        switch (@event)
        {
            case InputEventMouseMotion motion:
            {
                Vector2 travel = motion.GlobalPosition - _grabScreen;
                if (!_dragged && travel.Length() < DragThresholdPx) return;

                _dragged = true;
                Shift(new Vector2(_startOffsetTopLeft.X + travel.X - OffsetLeft,
                                  _startOffsetTopLeft.Y + travel.Y - OffsetTop));
                GetViewport().SetInputAsHandled();
                break;
            }

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
            {
                _pressing = false;
                if (!_dragged) return;

                ClampToViewport();
                Moved?.Invoke(Delta);
                // Swallow the release so the button never reports a click at the end of a drag.
                GetViewport().SetInputAsHandled();
                break;
            }
        }
    }
}
