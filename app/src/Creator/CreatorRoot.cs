using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DigitalDoll.Body;
using DigitalDoll.Core;
using DigitalDoll.Core.Body;
using Godot;

namespace DigitalDoll.Creator;

/// <summary>
/// The body editor: a 3D preview on the left and slider panels on the right. Only the
/// base body (with its fitted underwear) is edited here; clothing is a separate layer.
/// </summary>
public partial class CreatorRoot : Control
{
    public const string SavePath = "user://characters/current/body.doll";
    private const int UndoLimit = 50;
    private const float PanelWidth = 420f;

    private readonly Random _random = new();
    private BodyShape _shape = new();
    private readonly List<BodyShape> _undo = new();
    private readonly List<BodyShape> _redo = new();
    private readonly Dictionary<string, (HSlider Slider, Label Value)> _rows = new();

    private BodyMeshView _body = null!;
    private Camera3D _camera = null!;
    private TabContainer _tabs = null!;
    private Label _heightLabel = null!;
    private Label _status = null!;
    private ColorPickerButton _skinPicker = null!;
    private ColorPickerButton _underwearPicker = null!;
    private Button _undoButton = null!;
    private CheckButton _advancedToggle = null!;
    private CheckBox _symmetricToggle = null!;
    private Button _redoButton = null!;

    private bool _advanced;
    private bool _symmetric = true;
    private bool _dirty = true;
    private bool _sliderDragging;
    private bool _refreshing;
    private bool _zh;

    private float _yaw, _pitch = -0.05f, _distance = 3.6f, _targetY = 0.85f;
    private float _goalYaw, _goalPitch = -0.05f, _goalDistance = 3.6f, _goalTargetY = 0.85f;
    private bool _rotating, _panning;

    private CreatorSnapshot? _snapshot;

    public override void _Ready()
    {
        _snapshot = CreatorSnapshot.FromCommandLine(OS.GetCmdlineUserArgs());
        if (_snapshot?.Locale != null)
            TranslationServer.SetLocale(_snapshot.Locale);
        _zh = TranslationServer.GetLocale().StartsWith("zh", StringComparison.Ordinal);

        if (_snapshot == null)
            AppWindow.UseEditorMode(GetWindow());
        else
            GetWindow().Size = _snapshot.WindowSize;
        GetTree().AutoAcceptQuit = false;

        BuildLayout();
        LoadSaved();
        if (_snapshot != null)
            _snapshot.Apply(this);
        SyncToggles();
        RebuildSliders();
        RefreshColours();
        FrameBody("full");
        SnapCamera();
        SelfTest.OnSceneReady(this, "body editor");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            BackToDesktop();
    }

    public override void _Process(double delta)
    {
        if (_dirty)
        {
            _dirty = false;
            _body.Show(_shape);
            _heightLabel.Text = _zh
                ? $"身高 {_body.BodyHeight * 100f:F0} 公分"
                : $"Height {_body.BodyHeight * 100f:F0} cm";
            UpdateHeightRow();
        }

        float t = 1f - MathF.Exp(-12f * (float)delta);
        _yaw = Mathf.LerpAngle(_yaw, _goalYaw, t);
        _pitch = Mathf.Lerp(_pitch, _goalPitch, t);
        _distance = Mathf.Lerp(_distance, _goalDistance, t);
        _targetY = Mathf.Lerp(_targetY, _goalTargetY, t);
        PlaceCamera();

        _undoButton.Disabled = _undo.Count == 0;
        _redoButton.Disabled = _redo.Count == 0;
        _snapshot?.Tick(this);
    }

    // ---- State ---------------------------------------------------------------

    internal BodyShape Shape => _shape;

    internal void SetValue(string key, float value)
    {
        _shape.Set(key, value);
        _dirty = true;
    }

    internal void SetAdvanced(bool advanced) => _advanced = advanced;

    internal void ShowTab(string group)
    {
        int index = BodyCatalog.Groups.ToList().FindIndex(g => g.Id == group);
        if (index >= 0)
            _tabs.CurrentTab = index;
    }

    internal void SetView(float yaw, string frame)
    {
        _goalYaw = yaw;
        FrameBody(frame);
    }

    internal void SnapCamera()
    {
        _yaw = _goalYaw;
        _pitch = _goalPitch;
        _distance = _goalDistance;
        _targetY = _goalTargetY;
        _body.Show(_shape);
        PlaceCamera();
    }

    private void PushUndo()
    {
        _undo.Add(_shape.Clone());
        if (_undo.Count > UndoLimit)
            _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void Undo()
    {
        if (_undo.Count == 0)
            return;
        _redo.Add(_shape.Clone());
        _shape = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        AfterBulkChange();
    }

    private void Redo()
    {
        if (_redo.Count == 0)
            return;
        _undo.Add(_shape.Clone());
        _shape = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        AfterBulkChange();
    }

    /// <summary>Shows the toggles in the state the loaded body needs.</summary>
    private void SyncToggles()
    {
        // A body whose sides differ can only be shown with separate left / right sliders.
        if (BodyCatalog.Sliders.Any(d => d.Mirrored && MathF.Abs(_shape.Get(d.Id + ".l") - _shape.Get(d.Id + ".r")) > 1e-4f))
            _symmetric = false;
        _advancedToggle.SetPressedNoSignal(_advanced);
        _symmetricToggle.SetPressedNoSignal(_symmetric);
    }

    private void AfterBulkChange()
    {
        _dirty = true;
        bool wasSymmetric = _symmetric;
        SyncToggles();
        if (wasSymmetric != _symmetric)
            RebuildSliders();
        RefreshSliders();
        RefreshColours();
    }

    private void Randomise()
    {
        PushUndo();
        var next = new BodyShape { SkinColor = _shape.SkinColor, UnderwearColor = _shape.UnderwearColor };
        float R(float min, float max) => min + (float)_random.NextDouble() * (max - min);
        next.Set("gender", _shape.Get("gender"));
        next.Set("age", R(0f, 0.35f));
        next.Set("weight", R(0.2f, 0.8f));
        next.Set("muscle", R(0.25f, 0.75f));
        next.Set("height", R(0.2f, 0.8f));
        next.Set("proportions", R(0.4f, 1f));
        next.Set("breast-size", R(0.2f, 0.9f));
        next.Set("breast-firmness", R(0.3f, 0.9f));
        foreach (var def in BodyCatalog.Sliders.Where(d => d.Basic && d.Kind == SliderKind.Bipolar))
        {
            if (_random.NextDouble() < 0.5)
                continue;
            float value = R(-0.45f, 0.45f);
            foreach (var key in def.Keys)
                next.Set(key, value);
        }
        _shape = next;
        AfterBulkChange();
    }

    private void ResetAll()
    {
        PushUndo();
        var next = new BodyShape { SkinColor = _shape.SkinColor, UnderwearColor = _shape.UnderwearColor };
        next.Set("gender", _shape.Get("gender"));
        _shape = next;
        AfterBulkChange();
    }

    private void LoadSaved()
    {
        if (!FileAccess.FileExists(SavePath))
            return;
        try
        {
            _shape = BodyShape.FromJson(FileAccess.GetFileAsString(SavePath));
        }
        catch (Exception e)
        {
            GD.PushWarning($"Could not read {SavePath}: {e.Message}");
        }
    }

    /// <summary>Saves and shows this body on the desktop.</summary>
    private void BackToDesktop()
    {
        Save();
        if (_snapshot == null)
            SettingsStore.UseCharacter(CharacterSource.Body);
        AppWindow.OpenMascot(this);
    }

    private void Save()
    {
        if (_snapshot != null || SelfTest.Enabled)
            return;
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(SavePath.GetBaseDir()));
        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            _status.Text = Text("無法儲存", "Could not save") + $" ({FileAccess.GetOpenError()})";
            return;
        }
        file.StoreString(_shape.ToJson());
        _status.Text = Text("已儲存", "Saved") + " " + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    // ---- Camera --------------------------------------------------------------

    private void FrameBody(string frame)
    {
        float h = _body?.BodyHeight > 0.5f ? _body.BodyHeight : 1.7f;
        (_goalTargetY, _goalDistance) = frame switch
        {
            "upper" => (h * 0.72f, h * 1.05f),
            "lower" => (h * 0.3f, h * 1.25f),
            _ => (h * 0.5f, h * 2.15f),
        };
        _goalPitch = -0.05f;
    }

    private void PlaceCamera()
    {
        var target = new Vector3(0f, _targetY, 0f);
        var offset = new Vector3(
            MathF.Sin(_yaw) * MathF.Cos(_pitch),
            -MathF.Sin(_pitch),
            MathF.Cos(_yaw) * MathF.Cos(_pitch)) * _distance;
        _camera.LookAtFromPosition(target + offset, target, Vector3.Up);
    }

    private void OnViewportInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _goalDistance = Mathf.Max(0.6f, _goalDistance * 0.9f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _goalDistance = Mathf.Min(6f, _goalDistance * 1.1f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                _rotating = b.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } b:
                _panning = b.Pressed;
                break;
            case InputEventMouseMotion m when _rotating:
                _goalYaw -= m.Relative.X * 0.01f;
                _goalPitch = Mathf.Clamp(_goalPitch + m.Relative.Y * 0.005f, -0.9f, 0.9f);
                break;
            case InputEventMouseMotion m when _panning:
                _goalTargetY = Mathf.Clamp(_goalTargetY + m.Relative.Y * 0.003f * _goalDistance, 0f, 2.2f);
                break;
        }
    }

    // ---- Layout --------------------------------------------------------------

    private void BuildLayout()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var split = new HBoxContainer { Name = "Split" };
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        split.AddThemeConstantOverride("separation", 0);
        AddChild(split);

        var viewportBox = new SubViewportContainer { Stretch = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        viewportBox.GuiInput += OnViewportInput;
        split.AddChild(viewportBox);
        var viewport = new SubViewport { Msaa3D = Viewport.Msaa.Msaa4X };
        viewportBox.AddChild(viewport);
        BuildWorld(viewport);
        BuildViewportOverlay(viewportBox);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
        split.AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 14);
        panel.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        var title = new Label { Text = Text("捏身材", "Body editor") };
        title.AddThemeFontSizeOverride("font_size", 24);
        column.AddChild(title);
        column.AddChild(new Label
        {
            Text = Text("在素體狀態下調整身材；服裝是另一個圖層，之後再套上。",
                "Shape the base body here; clothing is a separate layer added later."),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1, 1, 1, 0.7f),
        });

        var toggles = new HBoxContainer();
        _advancedToggle = new CheckButton { Text = Text("進階模式", "Advanced") };
        _advancedToggle.Toggled += on => { _advanced = on; RebuildSliders(); };
        _symmetricToggle = new CheckBox { Text = Text("左右對稱", "Symmetric"), ButtonPressed = true };
        _symmetricToggle.Toggled += OnSymmetricToggled;
        toggles.AddChild(_advancedToggle);
        toggles.AddChild(_symmetricToggle);
        column.AddChild(toggles);

        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(_tabs);

        var colours = new GridContainer { Columns = 2 };
        colours.AddThemeConstantOverride("h_separation", 12);
        colours.AddChild(new Label { Text = Text("膚色", "Skin") });
        _skinPicker = new ColorPickerButton { CustomMinimumSize = new Vector2(120, 30), EditAlpha = false };
        _skinPicker.PickerCreated += () => AddSkinPresets(_skinPicker.GetPicker());
        _skinPicker.PopupClosed += () => SetColour(_skinPicker.Color, skin: true);
        colours.AddChild(_skinPicker);
        colours.AddChild(new Label { Text = Text("內衣顏色", "Underwear") });
        _underwearPicker = new ColorPickerButton { CustomMinimumSize = new Vector2(120, 30), EditAlpha = false };
        _underwearPicker.PopupClosed += () => SetColour(_underwearPicker.Color, skin: false);
        colours.AddChild(_underwearPicker);
        column.AddChild(colours);

        var edit = new HBoxContainer();
        _undoButton = AddButton(edit, Text("復原", "Undo"), Undo);
        _redoButton = AddButton(edit, Text("重做", "Redo"), Redo);
        AddButton(edit, Text("隨機", "Random"), Randomise);
        AddButton(edit, Text("全部重設", "Reset all"), ResetAll);
        column.AddChild(edit);

        var file = new HBoxContainer();
        AddButton(file, Text("儲存", "Save"), Save);
        AddButton(file, Text("回到桌面", "Back to desktop"), BackToDesktop);
        _status = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right };
        file.AddChild(_status);
        column.AddChild(file);
    }

    private void BuildWorld(SubViewport viewport)
    {
        var world = new Node3D { Name = "World" };
        viewport.AddChild(world);

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("#2a2e37"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("#c9cfdc"),
            AmbientLightEnergy = 0.25f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
        };
        world.AddChild(new WorldEnvironment { Environment = environment });

        _camera = new Camera3D { Fov = 30f, Near = 0.05f, Far = 50f, Current = true };
        world.AddChild(_camera);

        AddLight(world, new Vector3(-35f, 30f, 0f), 0.55f, new Color("#ffffff"), shadows: true);
        AddLight(world, new Vector3(-15f, -60f, 0f), 0.25f, new Color("#dbe6ff"), shadows: false);
        AddLight(world, new Vector3(-20f, 160f, 0f), 0.35f, new Color("#ffffff"), shadows: false);

        var floor = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.55f, Height = 0.01f, RadialSegments = 64 },
            Position = new Vector3(0f, -0.006f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#20232a"), Roughness = 0.9f },
        };
        world.AddChild(floor);

        _body = new BodyMeshView { Name = "Body" };
        world.AddChild(_body);
    }

    private static void AddLight(Node parent, Vector3 rotationDegrees, float energy, Color colour, bool shadows)
    {
        parent.AddChild(new DirectionalLight3D
        {
            RotationDegrees = rotationDegrees,
            LightEnergy = energy,
            LightColor = colour,
            ShadowEnabled = shadows,
        });
    }

    private void BuildViewportOverlay(Control parent)
    {
        var overlay = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        overlay.SetAnchorsPreset(LayoutPreset.TopLeft);
        overlay.Position = new Vector2(16, 16);
        parent.AddChild(overlay);

        var views = new HBoxContainer();
        AddButton(views, Text("正面", "Front"), () => SetView(0f, "full"));
        AddButton(views, Text("側面", "Side"), () => SetView(MathF.PI / 2f, "full"));
        AddButton(views, Text("背面", "Back"), () => SetView(MathF.PI, "full"));
        overlay.AddChild(views);

        var frames = new HBoxContainer();
        AddButton(frames, Text("全身", "Full body"), () => FrameBody("full"));
        AddButton(frames, Text("上半身", "Upper body"), () => FrameBody("upper"));
        AddButton(frames, Text("下半身", "Lower body"), () => FrameBody("lower"));
        overlay.AddChild(frames);

        _heightLabel = new Label();
        _heightLabel.AddThemeFontSizeOverride("font_size", 18);
        overlay.AddChild(_heightLabel);
        overlay.AddChild(new Label
        {
            Text = Text("左鍵拖曳：旋轉　右鍵拖曳：上下移動　滾輪：縮放",
                "Left drag: rotate · Right drag: move up/down · Wheel: zoom"),
            Modulate = new Color(1, 1, 1, 0.6f),
        });
    }

    private void RebuildSliders()
    {
        int current = Math.Max(_tabs.CurrentTab, 0);
        foreach (var child in _tabs.GetChildren())
        {
            _tabs.RemoveChild(child);
            child.QueueFree();
        }
        _rows.Clear();

        foreach (var group in BodyCatalog.Groups)
        {
            var scroll = new ScrollContainer
            {
                Name = _zh ? group.Zh : group.En,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            };
            var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            list.AddThemeConstantOverride("separation", 6);
            scroll.AddChild(list);
            _tabs.AddChild(scroll);

            foreach (var def in BodyCatalog.Sliders.Where(d => d.Group == group.Id && (d.Basic || _advanced)))
            {
                string label = _zh ? def.Zh : def.En;
                if (def.Mirrored && !_symmetric)
                {
                    AddRow(list, def, label + Text("（左）", " (left)"), new[] { def.Id + ".l" });
                    AddRow(list, def, label + Text("（右）", " (right)"), new[] { def.Id + ".r" });
                }
                else
                {
                    AddRow(list, def, label, def.Keys.ToArray());
                }
            }
        }
        _tabs.CurrentTab = Math.Min(current, _tabs.GetTabCount() - 1);
        RefreshSliders();
    }

    private void AddRow(VBoxContainer list, SliderDef def, string label, string[] keys)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 0);
        var header = new HBoxContainer();
        header.AddChild(new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true });
        var value = new Label { CustomMinimumSize = new Vector2(70, 0), HorizontalAlignment = HorizontalAlignment.Right };
        header.AddChild(value);
        var reset = new Button { Text = "↺", Flat = true, TooltipText = Text("恢復預設", "Reset") };
        header.AddChild(reset);
        box.AddChild(header);

        var slider = new HSlider
        {
            MinValue = def.Min,
            MaxValue = def.Max,
            Step = 0.01,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        slider.DragStarted += () => { _sliderDragging = true; PushUndo(); };
        slider.DragEnded += _ => _sliderDragging = false;
        slider.ValueChanged += v =>
        {
            if (_refreshing)
                return;
            if (!_sliderDragging)
                PushUndo();
            foreach (var key in keys)
                SetValue(key, (float)v);
            value.Text = FormatValue(def, (float)v);
        };
        reset.Pressed += () => slider.Value = def.Default;
        box.AddChild(slider);
        list.AddChild(box);

        _rows[keys[0]] = (slider, value);
    }

    private void RefreshSliders()
    {
        _refreshing = true;
        foreach (var (key, (slider, value)) in _rows)
        {
            float v = _shape.Get(key);
            slider.SetValueNoSignal(v);
            value.Text = FormatValue(BodyCatalog.ById[key.EndsWith(".l") || key.EndsWith(".r") ? key[..^2] : key], v);
        }
        _refreshing = false;
        UpdateHeightRow();
    }

    private void UpdateHeightRow()
    {
        if (_rows.TryGetValue("height", out var row) && _body.BodyHeight > 0f)
            row.Value.Text = $"{_body.BodyHeight * 100f:F0} cm";
    }

    private string FormatValue(SliderDef def, float v) => def.Id switch
    {
        "age" => _zh ? $"{25 + v * 65:F0} 歲" : $"{25 + v * 65:F0} y",
        "gender" => v < 0.34f ? Text("女", "F") : v > 0.66f ? Text("男", "M") : Text("中性", "Mixed") + $" {v * 100:F0}",
        "height" => _body.BodyHeight > 0f ? $"{_body.BodyHeight * 100f:F0} cm" : "",
        _ when def.Kind == SliderKind.Bipolar => v == 0f ? "0" : $"{v * 100:+0;-0}",
        _ => $"{v * 100:F0}",
    };

    private void OnSymmetricToggled(bool on)
    {
        _symmetric = on;
        if (on)
        {
            PushUndo();
            // Merge diverging sides to their average.
            foreach (var def in BodyCatalog.Sliders.Where(d => d.Mirrored))
            {
                float average = (_shape.Get(def.Id + ".l") + _shape.Get(def.Id + ".r")) * 0.5f;
                _shape.Set(def.Id + ".l", average);
                _shape.Set(def.Id + ".r", average);
            }
            _dirty = true;
        }
        RebuildSliders();
    }

    private void SetColour(Color colour, bool skin)
    {
        string html = "#" + colour.ToHtml(false).ToUpperInvariant();
        if ((skin ? _shape.SkinColor : _shape.UnderwearColor) == html)
            return;
        PushUndo();
        if (skin)
            _shape.SkinColor = html;
        else
            _shape.UnderwearColor = html;
        _dirty = true;
    }

    private void RefreshColours()
    {
        _skinPicker.Color = Color.FromHtml(_shape.SkinColor);
        _underwearPicker.Color = Color.FromHtml(_shape.UnderwearColor);
    }

    private static void AddSkinPresets(ColorPicker picker)
    {
        foreach (var html in new[] { "#F6D7C3", "#EBC0A1", "#E8B99B", "#D49A78", "#B97B56", "#8D5A3B", "#5E3A26" })
            picker.AddPreset(Color.FromHtml(html));
    }

    private static Button AddButton(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private string Text(string zh, string en) => _zh ? zh : en;
}
