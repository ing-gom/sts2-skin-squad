using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using Sts2SkinSquad.Localization;

namespace Sts2SkinSquad.Ui;

/// <summary>
/// The squad picker: one row per member, each cycling through the available looks with a thumbnail
/// so you can see what you are choosing.
///
/// A modal rather than an inline panel because the character-select screen's left side is already
/// taken by Skin Manager's panel, and because a picker with previews wants more room than that
/// strip has. Opened by <see cref="SquadButton"/>, hosted by the game's own
/// <see cref="NModalContainer"/> — which casts whatever it is handed to <c>IScreenContext</c>, so
/// implementing that interface is mandatory, not decorative.
/// </summary>
internal sealed partial class SquadModal : Control, IScreenContext
{
    private const int PreviewSize = 96;
    private const int PanelWidth = 860;
    private const int PreviewPanelWidth = 230;
    private const int PreviewPanelHeight = 300;

    /// <summary>Keeps the panel from collapsing when every slot row is hidden.</summary>
    private const int PlaceholderHeight = 72;

    /// <summary>Tab height plus room for the horizontal scrollbar that appears once tabs overflow.</summary>
    private const int TabStripHeight = 46;

    /// <summary>
    /// Cap on a preset name. Long enough for "Ironclad trio", short enough that a tab stays a tab —
    /// the strip scrolls, but a single 200-character name would still make one unreachable tab.
    /// </summary>
    private const int PresetNameMaxLength = 24;

    private readonly List<Label> _slotLabels = new();
    private readonly List<TextureRect> _slotPreviews = new();
    private readonly List<Control> _slotRows = new();

    private HBoxContainer? _tabRow;
    private LineEdit? _nameEdit;
    private Button? _deleteButton;
    private SkinPreview? _preview;
    private Label? _sizeLabel;
    private Button? _sizeUp;
    private Button? _sizeDown;
    private Label? _placeholder;
    private Label? _skinNote;
    private Button? _closeButton;
    private string[] _options = Array.Empty<string>();

    public Control? DefaultFocusedControl => _closeButton;

    /// <summary>The preview panel, so the verification harness can drive it and measure the result.</summary>
    internal SkinPreview? PreviewPanel => _preview;

    public static void Open()
    {
        try
        {
            NModalContainer? host = NModalContainer.Instance;
            if (host == null)
            {
                MainFile.Logger.Warn($"[{MainFile.ModId}] no modal container on this screen.");
                return;
            }
            if (host.OpenModal != null) return;   // the game refuses a second modal anyway

            host.Add(new SquadModal { Name = "SkinSquadModal" });
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] could not open the squad picker: {ex.Message}");
        }
    }

    public override void _Ready()
    {
        // The catalog scan is cached after the first call, so this is cheap on reopen.
        _options = Appearance.Options();

        // ★SetAnchorsAndOffsetsPreset, not SetAnchorsPreset: the latter sets anchors but leaves the
        // offsets alone, so the control keeps a zero size and everything inside it collapses into
        // the top-left corner instead of filling the screen.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;      // swallow clicks meant for the screen behind

        // Draw above Skin Manager's character-select UI. Its panel sits at ZIndex 1000 and its
        // hover preview at 3100, so a default-Z modal was being painted over by both.
        ZIndex = 4000;

        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
        // The default PanelContainer style is transparent here, which left the picker's text
        // floating over the character art. Give it its own opaque plate.
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.08f, 0.11f, 0.97f),
            BorderColor = new Color(0.75f, 0.66f, 0.42f),
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        });
        centre.AddChild(panel);

        var margin = new MarginContainer();
        foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 24);
        panel.AddChild(margin);

        // Preview on the left, controls on the right.
        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 20);
        margin.AddChild(body);

        _preview = new SkinPreview
        {
            CustomMinimumSize = new Vector2(PreviewPanelWidth, PreviewPanelHeight),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        body.AddChild(_preview);

        var column = new VBoxContainer();
        column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        column.AddThemeConstantOverride("separation", 12);
        body.AddChild(column);

        column.AddChild(new Label
        {
            Text = Strings.Get("modal_title"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        _tabRow = new HBoxContainer();
        _tabRow.AddThemeConstantOverride("separation", 4);

        // ★Scrolled, not laid out bare. An HBoxContainer reports the full width of its children as
        // its own minimum size, and a minimum size beats the panel's anchors — so a handful of
        // presets, or one long name, would widen the picker until it ran off the screen. A
        // ScrollContainer's minimum is independent of what it holds, which is the whole point.
        var tabScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, TabStripHeight),
        };
        tabScroll.AddChild(_tabRow);
        column.AddChild(tabScroll);

        column.AddChild(BuildPresetRow());
        column.AddChild(BuildSizeRow());
        column.AddChild(BuildSwapRow());
        column.AddChild(new HSeparator());

        // Shown when the rows below have nothing to show, so an empty picker explains itself
        // instead of looking broken.
        _placeholder = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, PlaceholderHeight),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _placeholder.AddThemeColorOverride("font_color", new Color(0.72f, 0.68f, 0.60f));
        column.AddChild(_placeholder);

        for (int i = 0; i < SkinSquadService.MaxDoubles; i++) column.AddChild(BuildSlotRow(i));

        column.AddChild(new HSeparator());

        // A standing note when no skin mods are installed: the picker still works with vanilla
        // characters, so this is information, not an error.
        _skinNote = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _skinNote.AddThemeColorOverride("font_color", new Color(0.62f, 0.58f, 0.52f));
        column.AddChild(_skinNote);
        _closeButton = new Button { Text = Strings.Get("modal_close") };
        _closeButton.Pressed += Close;
        column.AddChild(_closeButton);

        RebuildTabs();
        Refresh();
        // Open on the first member that actually has a look. Slot 1 defaults to "Same as me",
        // which has no figure and no portrait, so leading with it shows an empty panel.
        string? opening = SkinSquadService.SlotTokens
            .Take(Math.Max(SkinSquadService.DoubleCount, 1))
            .FirstOrDefault(t => t != Appearance.SelfToken && t != Appearance.RandomToken)
            ?? SkinSquadService.SlotTokens.ElementAtOrDefault(0);
        _preview?.Show(opening);
        _closeButton.GrabFocus();
    }

    /// <summary>Rename / delete for the preset currently selected in the tab row.</summary>
    private Control BuildPresetRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        _nameEdit = new LineEdit
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            PlaceholderText = Strings.Get("preset_name_placeholder"),
            MaxLength = PresetNameMaxLength,
        };

        // The name itself tracks every keystroke, so switching preset or saving from anywhere else
        // picks up what has been typed so far. Writing the file and rebuilding the tab strip do NOT:
        // both were running once per character, which is a disk write and a full teardown of every
        // tab button for each letter of a rename. Neither is interesting until the name settles.
        _nameEdit.TextChanged += text => SkinSquadService.Active.Name = text;
        _nameEdit.TextSubmitted += _ => CommitName();
        _nameEdit.FocusExited += CommitName;
        row.AddChild(_nameEdit);

        // An explicit Save beside Delete. The rename already commits on Enter or on focus leaving
        // the box, but neither is discoverable — a button says the name is something you confirm,
        // and gives the keyboard-averse a way to do it.
        var saveButton = new Button { Text = Strings.Get("preset_save"), CustomMinimumSize = new Vector2(96, 0) };
        saveButton.Pressed += CommitName;
        row.AddChild(saveButton);

        _deleteButton = new Button { Text = Strings.Get("preset_delete"), CustomMinimumSize = new Vector2(96, 0) };
        _deleteButton.Pressed += DeletePreset;
        row.AddChild(_deleteButton);
        return row;
    }

    /// <summary>
    /// Rebuilds the tab strip. Cheap enough to redo wholesale — there are at most a handful of
    /// presets, and rebuilding avoids having to reconcile added/removed/renamed tabs by hand.
    /// </summary>
    private void RebuildTabs()
    {
        if (_tabRow == null) return;
        foreach (Node child in _tabRow.GetChildren()) child.QueueFree();

        for (int i = 0; i < SkinSquadService.Presets.Count; i++)
        {
            int index = i;                                   // capture per iteration
            SquadPreset preset = SkinSquadService.Presets[i];
            bool active = i == SkinSquadService.ActivePreset;

            var tab = new Button
            {
                // An unnamed preset falls back to its position, translated. The fallback is display
                // only — nothing writes it back — so the label follows the game language instead of
                // freezing whichever language the preset happened to be created in.
                Text = string.IsNullOrWhiteSpace(preset.Name) ? Strings.Get("preset_default_name", i + 1) : preset.Name,
                Disabled = active,                           // the current tab is not a destination
                CustomMinimumSize = new Vector2(0, 32),
            };
            tab.Pressed += () => SelectPreset(index);
            _tabRow.AddChild(tab);
        }

        var add = new Button
        {
            Text = "+",
            CustomMinimumSize = new Vector2(40, 32),
            TooltipText = Strings.Get("preset_new_tip"),
        };
        add.Pressed += AddPreset;
        _tabRow.AddChild(add);
    }

    private void SelectPreset(int index)
    {
        SkinSquadService.ActivePreset = index;
        SquadConfig.Save();
        RebuildTabs();
        Refresh();
        _preview?.Show(SkinSquadService.SlotTokens.ElementAtOrDefault(0));
    }

    private void AddPreset()
    {
        SkinSquadService.Presets.Add(SquadPreset.Default());
        SelectPreset(SkinSquadService.Presets.Count - 1);
    }

    private void DeletePreset()
    {
        // Never drop the last one: the active preset IS the live configuration.
        if (SkinSquadService.Presets.Count <= 1) return;
        SkinSquadService.Presets.RemoveAt(SkinSquadService.ActivePreset);
        SelectPreset(Math.Min(SkinSquadService.ActivePreset, SkinSquadService.Presets.Count - 1));
    }

    /// <summary>
    /// Position-swap toggle. A global switch (like Dim back rows) rather than a per-preset value —
    /// it lives on <see cref="SkinSquadService"/>, not on the active preset, so it reads the same in
    /// every tab and is set once here. The picker is fresh-built on each open, so the initial state
    /// comes straight from the service; the handler writes back and persists.
    /// </summary>
    private Control BuildSwapRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var check = new CheckBox
        {
            Text = Strings.Get("cfg_swap_label"),
            ButtonPressed = SkinSquadService.SwapFront,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = Strings.Get("cfg_swap_desc"),
        };
        check.Toggled += on =>
        {
            SkinSquadService.SwapFront = on;
            SquadConfig.Save();
        };
        row.AddChild(check);
        return row;
    }

    private Control BuildSizeRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        row.AddChild(new Label { Text = Strings.Get("members_label"), SizeFlagsHorizontal = SizeFlags.ExpandFill });

        _sizeDown = MakeStep("-", () => ChangeSize(-1));
        row.AddChild(_sizeDown);

        _sizeLabel = new Label { CustomMinimumSize = new Vector2(40, 0), HorizontalAlignment = HorizontalAlignment.Center };
        row.AddChild(_sizeLabel);

        _sizeUp = MakeStep("+", () => ChangeSize(+1));
        row.AddChild(_sizeUp);
        return row;
    }

    private Control BuildSlotRow(int slot)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var preview = new TextureRect
        {
            CustomMinimumSize = new Vector2(PreviewSize, PreviewSize),
            // ExpandMode BEFORE any size assignment: a TextureRect left on the default mode reports
            // the raw texture size as its minimum and blows the row out to the image's width.
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        _slotPreviews.Add(preview);
        row.AddChild(preview);

        var name = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _slotLabels.Add(name);
        row.AddChild(name);

        row.AddChild(MakeStep("◀", () => Cycle(slot, -1)));
        row.AddChild(MakeStep("▶", () => Cycle(slot, +1)));

        // MouseFilter must be Stop for a container to receive hover at all — the default passes the
        // pointer straight through and the signals never fire.
        row.MouseFilter = MouseFilterEnum.Stop;
        row.MouseEntered += () => _preview?.Show(SkinSquadService.SlotTokens.ElementAtOrDefault(slot));

        _slotRows.Add(row);
        return row;
    }

    private static Button MakeStep(string text, Action onPressed)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(44, 0) };
        b.Pressed += onPressed;
        return b;
    }

    private void ChangeSize(int delta)
    {
        SkinSquadService.DoubleCount = Math.Clamp(SkinSquadService.DoubleCount + delta, 0, SkinSquadService.MaxDoubles);
        SquadConfig.Save();
        Refresh();

        // Growing back from empty should put something in the panel again rather than wait for the
        // pointer to wander over a row.
        if (SkinSquadService.DoubleCount > 0)
            _preview?.Show(SkinSquadService.SlotTokens.ElementAtOrDefault(SkinSquadService.DoubleCount - 1));
    }

    private void Cycle(int slot, int delta)
    {
        if (_options.Length == 0 || slot >= SkinSquadService.SlotTokens.Length) return;

        int current = Array.FindIndex(_options, o => o == SkinSquadService.SlotTokens[slot]);
        if (current < 0) current = 0;
        int next = ((current + delta) % _options.Length + _options.Length) % _options.Length;

        SkinSquadService.SlotTokens[slot] = _options[next];
        SquadConfig.Save();
        _preview?.Show(_options[next]);      // follow the arrows, not just the pointer
        Refresh();
    }

    /// <summary>Repaints from the current settings. Rows past the squad size are hidden rather than
    /// removed, so growing the squad does not need a rebuild.</summary>
    private void Refresh()
    {
        int count = SkinSquadService.DoubleCount;
        if (_sizeLabel != null) _sizeLabel.Text = $"{count} / {SkinSquadService.MaxDoubles}";

        if (_nameEdit != null && _nameEdit.Text != SkinSquadService.Active.Name)
            _nameEdit.Text = SkinSquadService.Active.Name;
        if (_deleteButton != null) _deleteButton.Disabled = SkinSquadService.Presets.Count <= 1;

        // Greyed out at the ends rather than silently clamping, so it is obvious that three is the
        // ceiling instead of the button just appearing to do nothing.
        if (_sizeUp != null) _sizeUp.Disabled = count >= SkinSquadService.MaxDoubles;
        if (_sizeDown != null) _sizeDown.Disabled = count <= 0;

        bool noMembers = SkinSquadService.DoubleCount <= 0;

        // Nothing to preview once every row is gone; without this the last hovered figure stayed
        // on screen and read as a frozen panel.
        if (noMembers) _preview?.ShowNothing();

        if (_placeholder != null)
        {
            _placeholder.Visible = noMembers;
            _placeholder.Text = Strings.Get("modal_empty");
        }

        if (_skinNote != null)
        {
            int skins = SkinCatalog.Variants.Count;
            _skinNote.Visible = skins == 0;
            _skinNote.Text = Strings.Get("modal_no_skins");
        }

        for (int i = 0; i < _slotRows.Count; i++)
        {
            bool active = i < SkinSquadService.DoubleCount;
            _slotRows[i].Visible = active;
            if (!active) continue;

            string token = SkinSquadService.SlotTokens.ElementAtOrDefault(i) ?? Appearance.SelfToken;

            // "4/22" tells you where you are in the full list — without it, cycling past a run of
            // skins for the same character gives no sense of position or of how many remain.
            // Indexed against _options, the very array Cycle() walks, so the number cannot drift
            // from what the arrows actually do.
            int at = Array.FindIndex(_options, o => string.Equals(o, token, StringComparison.OrdinalIgnoreCase)) + 1;
            string position = at > 0 ? $"   [{at}/{_options.Length}]" : "";

            _slotLabels[i].Text = $"{i + 1}.  {Appearance.DisplayName(token)}{position}";
            _slotPreviews[i].Texture = Appearance.Preview(token);
        }
    }

    /// <summary>Persists a finished rename and repaints the tab captions. Safe to call twice.</summary>
    private void CommitName()
    {
        SquadConfig.Save();
        RebuildTabs();
    }

    private void Close()
    {
        // Closing straight from the name box would otherwise lose the rename: the modal is freed
        // before FocusExited gets a chance to run. Only the write is needed here — the tab strip
        // is about to go away with the rest of the panel.
        SquadConfig.Save();

        try { NModalContainer.Instance?.Clear(); }
        catch (Exception ex) { MainFile.Logger.Warn($"[{MainFile.ModId}] closing the picker failed: {ex.Message}"); }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}
