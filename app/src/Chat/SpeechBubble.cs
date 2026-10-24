using Godot;

namespace DigitalDoll.Chat;

/// <summary>
/// The speech bubble: a small borderless, click-through window above the
/// character's head, sized to its text and pointing at the head with a tail.
/// </summary>
public partial class SpeechBubble : Window
{
    private const float MaxWidth = 340f;
    private const float MaxHeight = 280f;
    private const float TailHeight = 14f;

    private PanelContainer _panel = null!;
    private Label _source = null!;
    private Label _text = null!;
    private ScrollContainer _scroll = null!;
    private Control _tail = null!;
    private float _scale = 1f;
    private float _tailX = 0.5f;

    public override void _Ready()
    {
        Borderless = true;
        Transparent = true;
        TransparentBg = true;
        AlwaysOnTop = true;
        Unfocusable = true;
        MousePassthrough = true;
        Unresizable = true;
        Visible = false;

        // Any spare height goes above the bubble so the tail stays at the window's bottom edge.
        var box = new VBoxContainer { AnchorRight = 1f, AnchorBottom = 1f, Alignment = BoxContainer.AlignmentMode.End };
        box.AddThemeConstantOverride("separation", 0);
        AddChild(box);

        _panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(1f, 1f, 1f, 0.97f),
            BorderColor = new Color(0.45f, 0.62f, 0.85f),
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 8,
            ContentMarginBottom = 10,
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(14);
        _panel.AddThemeStyleboxOverride("panel", style);
        box.AddChild(_panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        _panel.AddChild(column);

        _source = new Label();
        _source.AddThemeColorOverride("font_color", new Color(0.4f, 0.5f, 0.65f));
        _source.AddThemeFontSizeOverride("font_size", 11);
        column.AddChild(_source);

        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(_scroll);
        _text = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _text.AddThemeColorOverride("font_color", new Color(0.13f, 0.15f, 0.2f));
        _text.AddThemeFontSizeOverride("font_size", 15);
        _scroll.AddChild(_text);

        _tail = new Control { CustomMinimumSize = new Vector2(0, TailHeight) };
        _tail.Draw += DrawTail;
        box.AddChild(_tail);
    }

    /// <summary>Shows <paramref name="text"/> with its tail pointing at <paramref name="head"/> (screen pixels).</summary>
    public void Say(string text, string source, Vector2I head)
    {
        int screen = DisplayServer.GetScreenFromRect(new Rect2(head, Vector2.One));
        _scale = Mathf.Clamp(DisplayServer.ScreenGetDpi(screen < 0 ? 0 : screen) / 96f, 1f, 3f);
        ContentScaleFactor = _scale;

        _source.Text = source;
        _source.Visible = source.Length > 0;
        _text.Text = text.Length == 0 ? "…" : text;

        // Width: the text on one line, up to the maximum; then wrap and measure the height.
        var font = _text.GetThemeFont("font");
        int size = _text.GetThemeFontSize("font_size");
        float lineWidth = 0f;
        foreach (var line in _text.Text.Split('\n'))
            lineWidth = Mathf.Max(lineWidth, font.GetStringSize(line, HorizontalAlignment.Left, -1, size).X);
        float width = Mathf.Clamp(lineWidth + 4f, 40f, MaxWidth);
        _text.CustomMinimumSize = new Vector2(width, 0f);
        _text.ResetSize();
        float textHeight = _text.GetMinimumSize().Y;
        _scroll.CustomMinimumSize = new Vector2(width, Mathf.Min(textHeight, MaxHeight));
        _scroll.ScrollVertical = (int)textHeight;

        var content = _panel.GetCombinedMinimumSize() + new Vector2(0f, TailHeight);
        var pixels = (Vector2I)(content * _scale).Ceil();
        Size = pixels;

        // Centred over the head, kept on the screen; the tail still points at the head.
        var usable = DisplayServer.ScreenGetUsableRect(screen < 0 ? 0 : screen);
        int x = Mathf.Clamp(head.X - pixels.X / 2, usable.Position.X, usable.End.X - pixels.X);
        int y = Mathf.Clamp(head.Y - pixels.Y - (int)(4 * _scale), usable.Position.Y, usable.End.Y - pixels.Y);
        Position = new Vector2I(x, y);
        _tailX = Mathf.Clamp((head.X - x) / (float)pixels.X, 0.15f, 0.85f);
        _tail.QueueRedraw();

        if (!Visible)
            Show();
    }

    public void Follow(Vector2I head)
    {
        if (!Visible)
            return;
        var usable = DisplayServer.ScreenGetUsableRect(DisplayServer.GetScreenFromRect(new Rect2(head, Vector2.One)) is int s and >= 0 ? s : 0);
        int x = Mathf.Clamp(head.X - Size.X / 2, usable.Position.X, usable.End.X - Size.X);
        int y = Mathf.Clamp(head.Y - Size.Y - (int)(4 * _scale), usable.Position.Y, usable.End.Y - Size.Y);
        var target = new Vector2I(x, y);
        if (target != Position)
        {
            Position = target;
            _tailX = Mathf.Clamp((head.X - x) / (float)Size.X, 0.15f, 0.85f);
            _tail.QueueRedraw();
        }
    }

    private void DrawTail()
    {
        float w = _tail.Size.X;
        float cx = w * _tailX;
        var fill = new Color(1f, 1f, 1f, 0.97f);
        var border = new Color(0.45f, 0.62f, 0.85f);
        var points = new[] { new Vector2(cx - 9f, -2f), new Vector2(cx + 9f, -2f), new Vector2(cx, TailHeight - 1f) };
        _tail.DrawColoredPolygon(points, fill);
        _tail.DrawLine(points[0] + new Vector2(0, 2), points[2], border, 2f, true);
        _tail.DrawLine(points[1] + new Vector2(0, 2), points[2], border, 2f, true);
    }
}
