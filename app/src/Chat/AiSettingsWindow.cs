using System;
using System.Linq;
using System.Threading;
using DigitalDoll.Core.Ai;
using Godot;

namespace DigitalDoll.Chat;

/// <summary>
/// "AI settings": the services the character can ask (added from presets), each
/// with its address, API key and model, plus the character's name and personality.
/// </summary>
public partial class AiSettingsWindow : Window
{
    private AiSettings _settings = new();
    private ProviderConfig? _current;

    private ItemList _list = null!;
    private PopupMenu _presets = null!;
    private Button _remove = null!;
    private Control _form = null!;
    private LineEdit _name = null!, _url = null!, _key = null!, _model = null!;
    private LinkButton _keyHelp = null!;
    private PopupMenu _models = null!;
    private Button _fetch = null!, _test = null!, _makeDefault = null!;
    private Label _status = null!;
    private LineEdit _characterName = null!, _personality = null!;
    private CancellationTokenSource? _work;

    /// <summary>Raised after the settings were saved.</summary>
    public event Action<AiSettings>? Saved;

    public override void _Ready()
    {
        Title = Tr("AI_TITLE");
        Visible = false;
        MinSize = new Vector2I(640, 460);
        CloseRequested += SaveAndClose;

        var margin = new MarginContainer { AnchorRight = 1f, AnchorBottom = 1f };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 14);
        AddChild(margin);
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 10);
        margin.AddChild(page);

        var top = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        top.AddThemeConstantOverride("separation", 14);
        page.AddChild(top);

        // Services.
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(190, 0) };
        top.AddChild(left);
        left.AddChild(new Label { Text = Tr("AI_SERVICES") });
        _list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _list.ItemSelected += index => Select((int)index);
        left.AddChild(_list);
        var buttons = new HBoxContainer();
        left.AddChild(buttons);
        var add = new MenuButton { Text = Tr("AI_ADD"), Flat = false };
        _presets = add.GetPopup();
        for (int i = 0; i < AiServices.Presets.Count; i++)
            _presets.AddItem(AiServices.Presets[i].Name, i);
        _presets.IdPressed += id => AddService(AiServices.Presets[(int)id]);
        buttons.AddChild(add);
        _remove = new Button { Text = Tr("AI_REMOVE") };
        _remove.Pressed += RemoveService;
        buttons.AddChild(_remove);

        // The selected service.
        var form = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        form.AddThemeConstantOverride("v_separation", 8);
        _form = form;
        top.AddChild(form);

        _name = Field(form, "AI_NAME", "");
        _name.TextChanged += text => { if (_current != null) { _current.Name = text; RefreshList(); } };
        _url = Field(form, "AI_URL", "https://…/v1");
        _url.TextChanged += text => { if (_current != null) _current.BaseUrl = text.Trim(); };

        form.AddChild(new Label { Text = Tr("AI_KEY") });
        var keyRow = new HBoxContainer();
        form.AddChild(keyRow);
        _key = new LineEdit { Secret = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, PlaceholderText = Tr("AI_KEY_PLACEHOLDER") };
        _key.TextChanged += text => { if (_current != null) KeyStore.Set(_current.Id, text); };
        keyRow.AddChild(_key);
        _keyHelp = new LinkButton { Text = Tr("AI_KEY_HELP") };
        _keyHelp.Pressed += () => { if (_keyHelp.Uri.Length > 0) OS.ShellOpen(_keyHelp.Uri); };
        keyRow.AddChild(_keyHelp);

        form.AddChild(new Label { Text = Tr("AI_MODEL") });
        var modelRow = new HBoxContainer();
        form.AddChild(modelRow);
        _model = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, PlaceholderText = Tr("AI_MODEL_PLACEHOLDER") };
        _model.TextChanged += text => { if (_current != null) _current.Model = text.Trim(); };
        modelRow.AddChild(_model);
        _fetch = new Button { Text = Tr("AI_MODEL_LIST") };
        _fetch.Pressed += FetchModels;
        modelRow.AddChild(_fetch);
        _models = new PopupMenu();
        _models.IndexPressed += index =>
        {
            _model.Text = _models.GetItemText((int)index);
            if (_current != null)
                _current.Model = _model.Text;
        };
        AddChild(_models);

        form.AddChild(new Control());
        var actions = new HBoxContainer();
        form.AddChild(actions);
        _makeDefault = new Button { Text = Tr("AI_MAKE_DEFAULT") };
        _makeDefault.Pressed += () => { if (_current != null) { _settings.DefaultProvider = _current.Id; RefreshList(); } };
        actions.AddChild(_makeDefault);
        _test = new Button { Text = Tr("AI_TEST") };
        _test.Pressed += Test;
        actions.AddChild(_test);

        form.AddChild(new Control());
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(320, 0) };
        form.AddChild(_status);

        page.AddChild(new HSeparator());
        var character = new GridContainer { Columns = 2 };
        character.AddThemeConstantOverride("v_separation", 8);
        page.AddChild(character);
        _characterName = Field(character, "AI_CHARACTER_NAME", "小桃");
        _characterName.TextChanged += text => _settings.CharacterName = text.Trim();
        _personality = Field(character, "AI_PERSONALITY", "");
        _personality.TextChanged += text => _settings.Personality = text.Trim();

        // Wrapping labels need a width, or they measure one character per line.
        var note = new Label { Text = Tr("AI_NOTE"), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(600, 0) };
        note.AddThemeFontSizeOverride("font_size", 12);
        note.AddThemeColorOverride("font_color", new Color(0.55f, 0.58f, 0.65f));
        page.AddChild(note);

        var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        page.AddChild(bottom);
        var save = new Button { Text = Tr("AI_SAVE") };
        save.Pressed += SaveAndClose;
        bottom.AddChild(save);
    }

    public void Open(AiSettings settings)
    {
        _settings = settings;
        _characterName.Text = settings.CharacterName;
        _personality.Text = settings.Personality;
        RefreshList();
        Select(0);

        int screen = DisplayServer.GetPrimaryScreen();
        float scale = Mathf.Clamp(DisplayServer.ScreenGetDpi(screen) / 96f, 1f, 3f);
        ContentScaleFactor = scale;
        var size = (Vector2I)(new Vector2(800, 520) * scale);
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        Size = new Vector2I(Mathf.Min(size.X, usable.Size.X - 40), Mathf.Min(size.Y, usable.Size.Y - 60));
        Position = usable.Position + (usable.Size - Size) / 2;
        Show();
        GrabFocus();
        if (settings.Providers.Count == 0)
            _status.Text = Tr("AI_START_HINT");
    }

    private LineEdit Field(GridContainer grid, string label, string placeholder)
    {
        grid.AddChild(new Label { Text = Tr(label) });
        var field = new LineEdit { PlaceholderText = placeholder, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddChild(field);
        return field;
    }

    private void RefreshList()
    {
        int selected = _current == null ? -1 : _settings.Providers.IndexOf(_current);
        _list.Clear();
        var fallback = _settings.Default;
        foreach (var p in _settings.Providers)
            _list.AddItem((p == fallback ? "★ " : "　 ") + (p.Name.Length > 0 ? p.Name : "?"));
        if (selected >= 0)
            _list.Select(selected);
    }

    private void Select(int index)
    {
        _current = index >= 0 && index < _settings.Providers.Count ? _settings.Providers[index] : null;
        _form.Visible = _current != null;
        _remove.Disabled = _current == null;
        _status.Text = "";
        if (_current == null)
            return;
        _list.Select(index);
        _name.SetBlockSignals(true);
        _url.SetBlockSignals(true);
        _key.SetBlockSignals(true);
        _model.SetBlockSignals(true);
        _name.Text = _current.Name;
        _url.Text = _current.BaseUrl;
        _key.Text = KeyStore.Get(_current.Id) ?? "";
        _model.Text = _current.Model;
        _name.SetBlockSignals(false);
        _url.SetBlockSignals(false);
        _key.SetBlockSignals(false);
        _model.SetBlockSignals(false);

        var preset = AiServices.Preset(_current.Preset);
        _key.Editable = preset?.NeedsKey != false;
        _key.PlaceholderText = Tr(preset?.NeedsKey == false ? "AI_KEY_NOT_NEEDED" : "AI_KEY_PLACEHOLDER");
        _keyHelp.Uri = preset?.KeyHelpUrl ?? "";
        _keyHelp.Visible = _keyHelp.Uri.Length > 0;
    }

    private void AddService(ServicePreset preset)
    {
        var config = AiServices.FromPreset(preset);
        // Unique handles for "@name" routing.
        string baseName = config.Name;
        for (int n = 2; _settings.Providers.Any(p => p.Handle == config.Handle); n++)
            config.Name = $"{baseName}{n}";
        _settings.Providers.Add(config);
        _settings.DefaultProvider ??= config.Id;
        RefreshList();
        Select(_settings.Providers.Count - 1);
        if (preset.NeedsKey)
            _key.GrabFocus();
    }

    private void RemoveService()
    {
        if (_current == null)
            return;
        KeyStore.Set(_current.Id, null);
        _settings.Providers.Remove(_current);
        if (_settings.DefaultProvider == _current.Id)
            _settings.DefaultProvider = _settings.Providers.FirstOrDefault()?.Id;
        _current = null;
        RefreshList();
        Select(0);
    }

    private async void FetchModels()
    {
        if (_current == null)
            return;
        _work?.Cancel();
        _work = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _status.Text = Tr("AI_LOADING_MODELS");
        try
        {
            var models = await AiServices.Create(_current, KeyStore.Get(_current.Id)).ListModelsAsync(_work.Token);
            _models.Clear();
            foreach (var model in models.Take(60))
                _models.AddItem(model);
            _status.Text = models.Count == 0 ? Tr("AI_NO_MODELS") : string.Format(Tr("AI_MODELS_FOUND"), models.Count);
            if (models.Count > 0)
            {
                var rect = _fetch.GetGlobalRect();
                _models.Position = Position + (Vector2I)((rect.Position + new Vector2(0, rect.Size.Y)) * ContentScaleFactor);
                _models.Popup();
            }
        }
        catch (Exception e)
        {
            _status.Text = ChatController.Describe(e, this);
        }
    }

    private async void Test()
    {
        if (_current == null)
            return;
        if (_current.Model.Length == 0)
        {
            _status.Text = Tr("AI_NEED_MODEL");
            return;
        }
        _work?.Cancel();
        _work = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _status.Text = Tr("AI_TESTING");
        _test.Disabled = true;
        try
        {
            var provider = AiServices.Create(_current, KeyStore.Get(_current.Id));
            var request = new ChatRequest(_current.Model, "Reply with one short friendly sentence.",
                new[] { new ChatTurn(ChatRole.User, "Hi!") }, 60);
            string reply = "";
            await foreach (var part in provider.StreamAsync(request, _work.Token))
                reply += part;
            _status.Text = string.Format(Tr("AI_TEST_OK"), reply.Trim().Length > 80 ? reply.Trim()[..80] + "…" : reply.Trim());
        }
        catch (Exception e)
        {
            _status.Text = ChatController.Describe(e, this);
        }
        finally
        {
            _test.Disabled = false;
        }
    }

    private void SaveAndClose()
    {
        _work?.Cancel();
        AiStore.Save(_settings);
        Saved?.Invoke(_settings);
        Hide();
    }
}
