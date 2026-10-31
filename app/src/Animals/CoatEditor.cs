using System;
using DigitalDoll.Core;
using Godot;

namespace DigitalDoll.Animals;

/// <summary>
/// The appearance window (right-click menu): ready-made coats, then colours and a pattern
/// to adjust. Every change shows on the animal straight away and is kept when the window
/// closes; Reset goes back to the way the animal comes.
/// </summary>
public partial class CoatEditor : Window
{
    private Species _species;
    private AnimalCoat _coat = new();
    private bool _filling;
    private HFlowContainer _presets = null!;
    private ColorPickerButton _main = null!, _second = null!, _accent = null!, _eye = null!, _patternColor = null!;
    private Control _secondRow = null!, _accentRow = null!, _patternRows = null!;
    private OptionButton _pattern = null!;
    private HSlider _size = null!, _amount = null!;

    /// <summary>The coat as it is now, on every change.</summary>
    public event Action<AnimalCoat>? Changed;

    /// <summary>The window was closed: time to keep the coat.</summary>
    public event Action<AnimalCoat>? Finished;

    public override void _Ready()
    {
        Title = Tr("COAT_TITLE");
        AlwaysOnTop = true;
        Visible = false;
        CloseRequested += Finish;
        MinSize = new Vector2I(340, 300);

        var margin = new MarginContainer { AnchorRight = 1f, AnchorBottom = 1f };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 12);
        AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        _presets = new HFlowContainer();
        column.AddChild(_presets);
        column.AddChild(new HSeparator());

        _main = Colour(column, "COAT_MAIN", out _);
        _second = Colour(column, "COAT_SECOND", out _secondRow);
        _accent = Colour(column, "COAT_ACCENT", out _accentRow);
        _eye = Colour(column, "COAT_EYE", out _);
        column.AddChild(new HSeparator());

        _pattern = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var kind in Enum.GetValues<CoatPattern>())
            _pattern.AddItem(Tr("COAT_PATTERN_" + kind.ToString().ToUpperInvariant()), (int)kind);
        _pattern.ItemSelected += _ => Edited();
        column.AddChild(Row("COAT_PATTERN", _pattern));

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 10);
        column.AddChild(rows);
        _patternRows = rows;
        _patternColor = Colour(rows, "COAT_PATTERN_COLOR", out _);
        _size = Slider(rows, "COAT_PATTERN_SIZE");
        _amount = Slider(rows, "COAT_PATTERN_AMOUNT");

        var buttons = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        column.AddChild(buttons);
        var reset = new Button { Text = Tr("COAT_RESET") };
        reset.Pressed += () => Show(AnimalCoat.Default(_species), notify: true);
        buttons.AddChild(reset);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var done = new Button { Text = Tr("COAT_DONE") };
        done.Pressed += Finish;
        buttons.AddChild(done);
    }

    /// <summary>Opens for this animal's coat; a model with fewer colours has fewer rows.</summary>
    public void Open(Species species, AnimalCoat coat, bool hasSecond, bool hasAccent, Vector2I near)
    {
        _species = species;
        _secondRow.Visible = hasSecond;
        _accentRow.Visible = hasAccent;

        foreach (var child in _presets.GetChildren())
        {
            _presets.RemoveChild(child);
            child.QueueFree();
        }
        bool chinese = TranslationServer.GetLocale().StartsWith("zh");
        foreach (var preset in CoatPresets.For(species))
        {
            var button = new Button { Text = chinese ? preset.Chinese : preset.English };
            button.Pressed += () => Show(preset.Coat, notify: true);
            _presets.AddChild(button);
        }
        Show(coat, notify: false);

        int screen = DisplayServer.GetScreenFromRect(new Rect2(near, Vector2.One));
        screen = screen < 0 ? 0 : screen;
        float scale = Mathf.Clamp(DisplayServer.ScreenGetDpi(screen) / 96f, 1f, 3f);
        ContentScaleFactor = scale;
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        var content = GetContentsMinimumSize();
        var size = (Vector2I)(new Vector2(MathF.Max(380f, content.X + 8f), MathF.Max(460f, content.Y + 8f)) * scale);
        size = new Vector2I(Math.Min(size.X, usable.Size.X - 20), Math.Min(size.Y, usable.Size.Y - 40));
        Size = size;
        Position = new Vector2I(
            Mathf.Clamp(near.X + (int)(40 * scale), usable.Position.X, usable.End.X - size.X),
            Mathf.Clamp(near.Y - size.Y / 2, usable.Position.Y + 30, usable.End.Y - size.Y - 10));
        Show();
        GrabFocus();
    }

    private void Show(AnimalCoat coat, bool notify)
    {
        _filling = true;
        _coat = coat;
        _main.Color = new Color(coat.Main);
        _second.Color = new Color(coat.Second);
        _accent.Color = new Color(coat.Accent);
        _eye.Color = new Color(coat.Eye);
        _patternColor.Color = new Color(coat.PatternColor);
        _pattern.Selected = _pattern.GetItemIndex((int)coat.Pattern);
        _size.Value = coat.PatternSize;
        _amount.Value = coat.PatternAmount;
        _patternRows.Visible = coat.Pattern != CoatPattern.None;
        _filling = false;
        if (notify)
            Changed?.Invoke(_coat);
    }

    private void Edited()
    {
        if (_filling)
            return;
        var pattern = (CoatPattern)_pattern.GetItemId(_pattern.Selected);
        _coat = new AnimalCoat
        {
            Main = Hex(_main.Color),
            Second = Hex(_second.Color),
            Accent = Hex(_accent.Color),
            Eye = Hex(_eye.Color),
            Pattern = pattern,
            PatternColor = Hex(_patternColor.Color),
            PatternSize = (float)_size.Value,
            PatternAmount = (float)_amount.Value,
        };
        _patternRows.Visible = pattern != CoatPattern.None;
        Changed?.Invoke(_coat);
    }

    private void Finish()
    {
        Hide();
        Finished?.Invoke(_coat);
    }

    private static string Hex(Color colour) => "#" + colour.ToHtml(false);

    private ColorPickerButton Colour(Container parent, string label, out Control row)
    {
        var picker = new ColorPickerButton { EditAlpha = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 28) };
        picker.ColorChanged += _ => Edited();
        row = Row(label, picker);
        parent.AddChild(row);
        return picker;
    }

    private HSlider Slider(Container parent, string label)
    {
        var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        slider.ValueChanged += _ => Edited();
        parent.AddChild(Row(label, slider));
        return slider;
    }

    private Control Row(string label, Control control)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = Tr(label), CustomMinimumSize = new Vector2(96, 0) });
        row.AddChild(control);
        return row;
    }
}
