using System;
using System.Collections.Generic;
using DigitalDoll.Core.Ai;
using Godot;

namespace DigitalDoll.Chat;

/// <summary>
/// The chat window: the whole history as speech bubbles (the user's on the right, the
/// character's on the left; each can be deleted, or all at once), and under it the AI
/// to ask, a text field, Actions and Send / Stop. Enter sends, Esc closes; "@name
/// question" asks a specific AI.
/// </summary>
public partial class ChatInput : Window
{
    private static readonly Color UserBubble = new(0.24f, 0.45f, 0.86f);
    private static readonly Color CharacterBubble = new(0.24f, 0.26f, 0.31f);
    private static readonly Color MetaText = new(0.72f, 0.75f, 0.82f);

    private OptionButton _provider = null!;
    private LineEdit _field = null!;
    private Button _send = null!;
    private Label _hint = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _list = null!;
    private Label _empty = null!;
    private Button _clear = null!;
    private ConfirmationDialog _confirm = null!;
    private readonly List<string?> _providerIds = new();
    private IReadOnlyList<ChatEntry> _entries = Array.Empty<ChatEntry>();
    private bool _busy;

    /// <summary>The text and the chosen AI (null for the default).</summary>
    public event Action<string, string?>? Submitted;
    public event Action? StopRequested;

    /// <summary>An action picked from the Actions button (an id as the chat commands use).</summary>
    public event Action<string>? ActionPicked;

    /// <summary>A message's delete button was pressed (its <see cref="ChatEntry.Id"/>).</summary>
    public event Action<long>? DeleteRequested;

    /// <summary>"Clear all" was confirmed.</summary>
    public event Action? ClearRequested;

    public override void _Ready()
    {
        Title = Tr("CHAT_TITLE");
        AlwaysOnTop = true;
        Visible = false;
        CloseRequested += Hide;
        MinSize = new Vector2I(420, 300);

        var margin = new MarginContainer { AnchorRight = 1f, AnchorBottom = 1f };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 10);
        AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        // History: a title, Clear all, and the bubbles.
        var header = new HBoxContainer();
        column.AddChild(header);
        header.AddChild(new Label { Text = Tr("CHAT_HISTORY"), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _clear = new Button { Text = Tr("CHAT_CLEAR"), Flat = true };
        _clear.Pressed += () => _confirm.PopupCentered();
        header.AddChild(_clear);
        _confirm = new ConfirmationDialog { DialogText = Tr("CHAT_CLEAR_CONFIRM"), Title = Tr("CHAT_CLEAR") };
        _confirm.Confirmed += () => ClearRequested?.Invoke();
        AddChild(_confirm);

        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, 160),
        };
        column.AddChild(_scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 6);
        _scroll.AddChild(_list);
        _empty = new Label
        {
            Text = Tr("CHAT_NO_HISTORY"),
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _empty.AddThemeColorOverride("font_color", MetaText);

        // The question.
        var row = new HBoxContainer();
        column.AddChild(row);
        _provider = new OptionButton { TooltipText = Tr("CHAT_PROVIDER_TIP") };
        row.AddChild(_provider);
        _field = new LineEdit
        {
            PlaceholderText = Tr("CHAT_PLACEHOLDER"),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(200, 0),
            ClearButtonEnabled = true,
        };
        _field.TextSubmitted += _ => Send();
        row.AddChild(_field);
        // Actions right next to the field: pick one instead of typing it.
        var actionMenu = ActionMenu.Create(key => Tr(key), id => ActionPicked?.Invoke(id));
        AddChild(actionMenu);
        var actions = new Button { Text = Tr("CHAT_ACTIONS"), TooltipText = Tr("CHAT_ACTIONS_TIP") };
        actions.Pressed += () =>
        {
            var at = actions.GetScreenPosition() + new Vector2(0f, actions.Size.Y * ContentScaleFactor);
            actionMenu.Position = (Vector2I)at;
            actionMenu.Popup();
        };
        row.AddChild(actions);
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

        Rebuild();
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
        // Wide enough for the AI list, field and buttons in one row, tall enough for a
        // good stretch of the history; never taller than the screen allows.
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        var content = GetContentsMinimumSize();
        var size = (Vector2I)(new Vector2(MathF.Max(600f, content.X + 8f), MathF.Max(520f, content.Y + 6f)) * scale);
        size = new Vector2I(Math.Min(size.X, usable.Size.X - 20), Math.Min(size.Y, usable.Size.Y - 40));
        Size = size;
        Position = new Vector2I(
            Mathf.Clamp(near.X - size.X / 2, usable.Position.X, usable.End.X - size.X),
            Mathf.Clamp(near.Y + (int)(20 * scale), usable.Position.Y + 30, usable.End.Y - size.Y - 10));
        Show();
        GrabFocus();
        _field.GrabFocus();
        ScrollToEnd();
    }

    /// <summary>Shows the history (oldest first); called again whenever it changes.</summary>
    public void ShowHistory(IReadOnlyList<ChatEntry> entries)
    {
        _entries = entries;
        if (_list != null)
            Rebuild();
    }

    /// <summary>While a reply streams the button stops it.</summary>
    public void SetBusy(bool busy)
    {
        _busy = busy;
        if (_send != null)
            _send.Text = Tr(busy ? "CHAT_STOP" : "CHAT_SEND");
    }

    private void Rebuild()
    {
        // The "no messages yet" line stays in the list; only the bubbles are replaced.
        if (_empty.GetParent() == null)
            _list.AddChild(_empty);
        foreach (var child in _list.GetChildren())
        {
            if (child != _empty)
            {
                _list.RemoveChild(child);
                child.QueueFree();
            }
        }
        _empty.Visible = _entries.Count == 0;
        _clear.Disabled = _entries.Count == 0;
        foreach (var entry in _entries)
            _list.AddChild(Bubble(entry));
        ScrollToEnd();
    }

    /// <summary>A message: a rounded bubble with its text, time (and AI), and a delete button beside it.</summary>
    private Control Bubble(ChatEntry entry)
    {
        bool mine = entry.Role == ChatRole.User;
        var line = new HBoxContainer();

        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3f };
        var style = new StyleBoxFlat { BgColor = mine ? UserBubble : CharacterBubble };
        style.SetCornerRadiusAll(10);
        style.SetContentMarginAll(8);
        panel.AddThemeStyleboxOverride("panel", style);
        var inside = new VBoxContainer();
        inside.AddThemeConstantOverride("separation", 2);
        panel.AddChild(inside);
        var text = new Label { Text = entry.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        text.AddThemeColorOverride("font_color", Colors.White);
        inside.AddChild(text);
        var local = entry.Time.ToLocalTime();
        string when = local.Date == DateTimeOffset.Now.Date ? local.ToString("HH:mm") : local.ToString("yyyy/MM/dd HH:mm");
        var meta = new Label
        {
            Text = mine || entry.Source.Length == 0 ? when : $"{entry.Source} · {when}",
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };
        meta.AddThemeFontSizeOverride("font_size", 10);
        meta.AddThemeColorOverride("font_color", MetaText);
        inside.AddChild(meta);

        var delete = new Button { Text = "×", Flat = true, TooltipText = Tr("CHAT_DELETE_TIP"), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        long id = entry.Id;
        delete.Pressed += () => DeleteRequested?.Invoke(id);
        var room = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f };

        // Mine on the right, the character's on the left, like a messaging app.
        if (mine)
        {
            line.AddChild(room);
            line.AddChild(delete);
            line.AddChild(panel);
        }
        else
        {
            line.AddChild(panel);
            line.AddChild(delete);
            line.AddChild(room);
        }
        return line;
    }

    /// <summary>Scrolls to the newest message once the list has been laid out.</summary>
    private void ScrollToEnd() => Callable.From(() =>
    {
        if (IsInstanceValid(_scroll))
            _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
    }).CallDeferred();

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
