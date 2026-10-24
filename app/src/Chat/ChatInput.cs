using System;
using System.Collections.Generic;
using DigitalDoll.Core.Ai;
using Godot;

namespace DigitalDoll.Chat;

/// <summary>
/// The chat box: a small window with the AI to ask, a text field and Send / Stop.
/// Enter sends, Esc closes; "@name question" asks a specific AI.
/// </summary>
public partial class ChatInput : Window
{
    private OptionButton _provider = null!;
    private LineEdit _field = null!;
    private Button _send = null!;
    private Label _hint = null!;
    private readonly List<string?> _providerIds = new();
    private bool _busy;

    /// <summary>The text and the chosen AI (null for the default).</summary>
    public event Action<string, string?>? Submitted;
    public event Action? StopRequested;

    public override void _Ready()
    {
        Title = Tr("CHAT_TITLE");
        AlwaysOnTop = true;
        Visible = false;
        CloseRequested += Hide;

        var margin = new MarginContainer { AnchorRight = 1f, AnchorBottom = 1f };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 10);
        AddChild(margin);
        var column = new VBoxContainer();
        margin.AddChild(column);

        var row = new HBoxContainer();
        column.AddChild(row);
        _provider = new OptionButton { TooltipText = Tr("CHAT_PROVIDER_TIP") };
        row.AddChild(_provider);
        _field = new LineEdit
        {
            PlaceholderText = Tr("CHAT_PLACEHOLDER"),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(280, 0),
            ClearButtonEnabled = true,
        };
        _field.TextSubmitted += _ => Send();
        row.AddChild(_field);
        _send = new Button { Text = Tr("CHAT_SEND") };
        _send.Pressed += () =>
        {
            if (_busy)
                StopRequested?.Invoke();
            else
                Send();
        };
        row.AddChild(_send);

        _hint = new Label { Text = Tr("CHAT_HINT") };
        _hint.AddThemeFontSizeOverride("font_size", 11);
        _hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.58f, 0.65f));
        column.AddChild(_hint);
    }

    public override void _Input(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            if (_busy)
                StopRequested?.Invoke();
            else
                Hide();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Opens near the character (screen pixels) with the AI list refreshed.</summary>
    public void Open(AiSettings settings, Vector2I near)
    {
        _provider.Clear();
        _providerIds.Clear();
        var fallback = settings.Default;
        _provider.AddItem(fallback == null ? Tr("CHAT_NO_PROVIDER") : string.Format(Tr("CHAT_DEFAULT_PROVIDER"), fallback.Name));
        _providerIds.Add(null);
        foreach (var p in settings.Providers)
        {
            _provider.AddItem(p.Name);
            _providerIds.Add(p.Id);
        }

        int screen = DisplayServer.GetScreenFromRect(new Rect2(near, Vector2.One));
        screen = screen < 0 ? 0 : screen;
        float scale = Mathf.Clamp(DisplayServer.ScreenGetDpi(screen) / 96f, 1f, 3f);
        ContentScaleFactor = scale;
        var size = (Vector2I)(new Vector2(520, 96) * scale);
        Size = size;
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        Position = new Vector2I(
            Mathf.Clamp(near.X - size.X / 2, usable.Position.X, usable.End.X - size.X),
            Mathf.Clamp(near.Y + (int)(20 * scale), usable.Position.Y + 30, usable.End.Y - size.Y - 10));
        Show();
        GrabFocus();
        _field.GrabFocus();
    }

    /// <summary>While a reply streams the button stops it.</summary>
    public void SetBusy(bool busy)
    {
        _busy = busy;
        if (_send != null)
            _send.Text = Tr(busy ? "CHAT_STOP" : "CHAT_SEND");
    }

    private void Send()
    {
        var text = _field.Text.Trim();
        if (text.Length == 0 || _busy)
            return;
        _field.Clear();
        int index = _provider.Selected;
        Submitted?.Invoke(text, index >= 0 && index < _providerIds.Count ? _providerIds[index] : null);
    }
}
