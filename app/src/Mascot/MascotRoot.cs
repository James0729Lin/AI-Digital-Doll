using System;
using System.Collections.Generic;
using System.Linq;
using DigitalDoll.Body;
using DigitalDoll.Character;
using DigitalDoll.Core;
using DigitalDoll.Core.Body;
using DigitalDoll.Core.Mascot;
using DigitalDoll.Creator;
using DigitalDoll.Platform;
using Godot;

namespace DigitalDoll.Mascot;

/// <summary>
/// Desktop mode: a transparent, always-on-top window covering the usable area of a
/// screen, with the character standing on the taskbar. Only the area around the
/// character receives the mouse; everything else passes through to the desktop.
/// </summary>
public partial class MascotRoot : Node3D
{
    private const string DefaultCharacter = "res://assets/characters/godette/Godette_vrm_v4.vrm";
    private const string SavedCharacter = "user://characters/current.vrm";
    private const float CharacterHeightPoints = 300f;
    private const float FacingTurn = 1.1f;
    private const float HitMargin = 5f;
    private const float HitSlack = 6f;

    private const int MenuCallOver = 0;
    private const int MenuLoad = 1;
    private const int MenuReset = 2;
    private const int MenuQuit = 3;
    private const int MenuCreator = 4;
    private const int MenuUseBody = 5;

    private Camera3D _camera = null!;
    private Node3D _pivot = null!;
    private PopupMenu _menu = null!;
    private FileDialog _fileDialog = null!;

    private readonly Random _random = new();
    private MascotBrain? _brain;
    private Node3D? _model;
    private HumanoidRig? _rig;
    private ProceduralAnimator? _animator;
    private CharacterHitShape? _hitShape;

    private Vector2I _windowSize;
    private float _pixelsPerMetre = 180f;
    private float _heightPixels = CharacterHeightPoints;
    private float _modelHeight = 1.6f;
    private float _yaw;
    private float _screenCheck;
    private float _windowsCheck;
    private Vector2[] _region = Array.Empty<Vector2>();

    private bool _dragging;
    private Vector2 _grabOffset;
    private Vector2 _lastCursor;
    private Vector2 _dragVelocity;

    private Snapshot? _snapshot;

    // The --creator argument opens the editor once at start-up, not on every return.
    private static bool _creatorRequested;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Camera");
        _pivot = GetNode<Node3D>("Pivot");
        _menu = GetNode<PopupMenu>("TrayMenu");
        _fileDialog = GetNode<FileDialog>("OpenCharacterDialog");

        var args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--creator") >= 0 && !_creatorRequested)
        {
            _creatorRequested = true;
            AppWindow.OpenCreator(this);
            return;
        }

        _snapshot = Snapshot.FromCommandLine(args);
        if (_snapshot != null)
        {
            ConfigureSnapshotWindow(_snapshot);
        }
        else
        {
            AppWindow.UseDesktopMode(GetWindow());
            ConfigureDesktopWindow();
        }

        BuildMenu();
        GetNode<StatusIndicator>("Tray").Pressed += OnTrayPressed;
        _fileDialog.FileSelected += path => LoadCharacter(path, persist: true);
        GetWindow().FilesDropped += files =>
        {
            var vrm = files.FirstOrDefault(f => f.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase));
            if (vrm != null)
                LoadCharacter(vrm, persist: true);
        };

        if (!LoadStartCharacter())
            LoadCharacter(DefaultCharacter, persist: false);

        _snapshot?.Begin(_brain!, _windowSize);
        SelfTest.OnSceneReady(this, "desktop");
    }

    public override void _Process(double delta)
    {
        float dt = _snapshot != null ? 1f / 60f : (float)delta;
        if (_brain == null || _rig == null || _animator == null)
            return;

        if (_snapshot == null)
        {
            _screenCheck -= dt;
            if (_screenCheck <= 0f)
            {
                _screenCheck = 2f;
                ConfigureDesktopWindow();
            }
            _windowsCheck -= dt;
            if (_windowsCheck <= 0f)
            {
                // Windows move, open and close; a few refreshes a second keeps the
                // mascot on top of them without a noticeable cost.
                _windowsCheck = 0.25f;
                _brain.SetSurroundings(BuildSurroundings());
            }
        }

        if (_dragging)
            UpdateDrag(dt);

        _brain.Update(dt);
        PlaceCharacter(dt);

        _animator.LookYaw = 0f;
        _animator.LookPitch = 0f;
        if (_brain.State == MascotState.Idle && _snapshot == null)
            AimHeadAtCursor();
        _animator.Update(dt, _brain.State, _brain.VelocityX / _pixelsPerMetre, _dragVelocity.X / _pixelsPerMetre,
            _brain.State == MascotState.Climb ? _brain.VelocityY / _pixelsPerMetre : 0f);

        if (_snapshot == null)
        {
            UpdateHitRegion();
            bool calm = _brain.State == MascotState.Idle && !_dragging;
            Engine.MaxFps = calm ? 30 : 60;
        }
        else
        {
            _snapshot.Tick(this);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_brain == null || _snapshot != null)
            return;

        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left && button.Pressed)
            {
                _dragging = true;
                _lastCursor = CursorInWindow();
                _grabOffset = _lastCursor - new Vector2(_brain.X, _brain.Y);
                _dragVelocity = Vector2.Zero;
                _brain.BeginDrag();
                SetMousePassthrough(Array.Empty<Vector2>());
            }
            else if (button.ButtonIndex == MouseButton.Left && !button.Pressed && _dragging)
            {
                _dragging = false;
                _brain.EndDrag(_dragVelocity.X, Mathf.Min(_dragVelocity.Y, 0f));
            }
            else if (button.ButtonIndex == MouseButton.Right && button.Pressed)
            {
                ShowMenu(DisplayServer.MouseGetPosition());
            }
        }
    }

    private void ConfigureDesktopWindow()
    {
        var window = GetWindow();
        int screen = window.CurrentScreen;
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        // One pixel short of the work area: a window that exactly covers it is treated as
        // full screen, which stops an auto-hidden taskbar from appearing.
        var size = new Vector2I(usable.Size.X, Math.Max(usable.Size.Y - 1, 100));
        float scale = DisplayServer.ScreenGetDpi(screen) / 96f;

        bool changed = window.Position != usable.Position || window.Size != size || _windowSize != size;
        if (!changed && _brain != null)
            return;

        window.Position = usable.Position;
        window.Size = size;
        _windowSize = size;
        _heightPixels = CharacterHeightPoints * Mathf.Clamp(scale, 1f, 3f);
        UpdateCamera();
        _brain?.SetSurroundings(BuildSurroundings());

        if (_brain == null)
            WindowsNative.HideFromTaskbar(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle));
    }

    private void ConfigureSnapshotWindow(Snapshot snapshot)
    {
        var window = GetWindow();
        window.Size = snapshot.WindowSize;
        _windowSize = snapshot.WindowSize;
        _heightPixels = snapshot.WindowSize.Y * 0.8f;
    }

    /// <summary>The floor, the screen edges and the tops of the visible windows, in window pixels.</summary>
    private Surroundings BuildSurroundings()
    {
        if (_snapshot != null)
            return Surroundings.Screen(_windowSize.X, _windowSize.Y, _heightPixels);

        var origin = GetWindow().Position;
        long own = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        var windows = new List<DesktopWindow>();
        foreach (var (id, bounds) in WindowsNative.VisibleWindows(own))
        {
            var local = new Rect2I(bounds.Position - origin, bounds.Size);
            if (!local.Intersects(new Rect2I(Vector2I.Zero, _windowSize)))
                continue;
            windows.Add(new DesktopWindow(id, local.Position.X, local.Position.Y, local.End.X, local.End.Y));
        }
        return Surroundings.FromWindows(_windowSize.X, _windowSize.Y, _heightPixels, windows);
    }

    private void UpdateCamera()
    {
        if (_rig == null)
            return;
        _pixelsPerMetre = _heightPixels / _modelHeight;
        _camera.Projection = Camera3D.ProjectionType.Orthogonal;
        _camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        _camera.Size = _windowSize.Y / _pixelsPerMetre;
        _camera.Position = new Vector3(0f, 0f, 10f);
    }

    private bool LoadStartCharacter()
    {
        if (_snapshot != null)
        {
            return _snapshot.Body != null
                ? AttachCharacter(BodyCharacter.Create(_snapshot.Body), "body (snapshot)")
                : LoadCharacter(_snapshot.CharacterPath ?? DefaultCharacter, persist: false);
        }

        var settings = SettingsStore.Load();
        return settings.Character switch
        {
            CharacterSource.Body => LoadBody(),
            CharacterSource.Vrm => FileAccess.FileExists(SavedCharacter) && LoadCharacter(SavedCharacter, persist: false),
            _ => LoadCharacter(DefaultCharacter, persist: false),
        };
    }

    private bool LoadCharacter(string path, bool persist)
    {
        var model = VrmLoader.Load(path);
        if (model == null || !AttachCharacter(model, path))
            return false;
        if (persist)
        {
            SaveCharacter(path);
            SettingsStore.UseCharacter(CharacterSource.Vrm);
        }
        return true;
    }

    /// <summary>Shows the body made in the body editor, rigged for the desktop.</summary>
    private bool LoadBody()
    {
        if (!FileAccess.FileExists(CreatorRoot.SavePath))
            return false;
        try
        {
            var shape = BodyShape.FromJson(FileAccess.GetFileAsString(CreatorRoot.SavePath));
            return AttachCharacter(BodyCharacter.Create(shape), CreatorRoot.SavePath);
        }
        catch (Exception e)
        {
            GD.PushError($"Could not load the edited body: {e.Message}");
            return false;
        }
    }

    private bool AttachCharacter(Node3D model, string label)
    {
        _pivot.AddChild(model);
        var rig = HumanoidRig.Create(model);
        if (rig == null)
        {
            GD.PushError($"{label} has no humanoid skeleton");
            model.QueueFree();
            return false;
        }

        _model?.QueueFree();
        _model = model;
        _rig = rig;
        _hitShape = CharacterHitShape.Measure(model, rig.Skeleton);
        _region = Array.Empty<Vector2>();
        var expressions = ExpressionRig.Create(model);
        _animator = new ProceduralAnimator(rig, expressions, _random);
        GD.Print($"Loaded {label}: expressions [{string.Join(", ", expressions.Names)}]");

        // Turn the model so that its forward axis matches the pivot's +Z (toward the camera).
        var forward = model.GlobalBasis.Inverse() * (rig.Skeleton.GlobalBasis * rig.Forward);
        model.Rotation = new Vector3(0f, -Mathf.Atan2(forward.X, forward.Z), 0f);

        _modelHeight = MeasureHeight(model, _pivot);
        UpdateCamera();
        if (_brain == null)
            _brain = new MascotBrain(
                new MascotTuning
                {
                    WalkSpeed = 0.55f * _pixelsPerMetre,
                    ClimbSpeed = 0.32f * _pixelsPerMetre,
                    Height = _heightPixels,
                },
                _random, _windowSize.X * 0.7f, BuildSurroundings());
        return true;
    }

    /// <summary>Height of all visible meshes (hair and shoes included) in pivot space.</summary>
    private static float MeasureHeight(Node3D model, Node3D space)
    {
        var toSpace = space.GlobalTransform.AffineInverse();
        float top = float.MinValue, bottom = float.MaxValue;
        foreach (var node in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mesh = (MeshInstance3D)node;
            var box = toSpace * mesh.GlobalTransform * mesh.GetAabb();
            top = Mathf.Max(top, box.End.Y);
            bottom = Mathf.Min(bottom, box.Position.Y);
        }
        return top > bottom ? top - bottom : 1.6f;
    }

    private static void SaveCharacter(string path)
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://characters"));
        string target = ProjectSettings.GlobalizePath(SavedCharacter);
        if (ProjectSettings.GlobalizePath(path) != target)
            DirAccess.CopyAbsolute(path, target);
    }

    private void PlaceCharacter(float dt)
    {
        var brain = _brain!;
        float x = _snapshot != null ? _windowSize.X * 0.5f : brain.X;
        _pivot.Position = ScreenToWorld(new Vector2(x, brain.Y));

        float target = brain.State switch
        {
            MascotState.Walk => brain.Facing * FacingTurn,
            MascotState.Falling or MascotState.Jump => brain.Facing * FacingTurn * 0.6f,
            // Side on to the screen edge it is climbing.
            MascotState.Climb or MascotState.Cling => brain.WallSide * MathF.PI / 2f,
            MascotState.Dragged or MascotState.Sit => 0f,
            _ => brain.Facing * 0.3f,
        };
        if (_snapshot?.Yaw is float yaw)
            target = yaw;
        _yaw = Mathf.Lerp(_yaw, target, 1f - MathF.Exp(-8f * dt));
        _pivot.Rotation = new Vector3(0f, _yaw, 0f);
    }

    private void AimHeadAtCursor()
    {
        var rig = _rig!;
        var head = rig.BoneGlobalPosition("Head");
        var cursor = ScreenToWorld(CursorInWindow()) + new Vector3(0f, 0f, 0.6f);
        var local = rig.Skeleton.GlobalBasis.Inverse() * (cursor - head);
        float forward = local.Dot(rig.Forward);
        float side = local.Dot(rig.Left);
        float up = local.Dot(rig.Up);
        _animator!.LookYaw = Mathf.Atan2(side, Mathf.Max(forward, 0.05f));
        _animator.LookPitch = Mathf.Atan2(up, new Vector2(forward, side).Length());
    }

    private void UpdateDrag(float dt)
    {
        var cursor = CursorInWindow();
        if (dt > 0f)
            _dragVelocity = _dragVelocity.Lerp((cursor - _lastCursor) / dt, 1f - MathF.Exp(-12f * dt));
        _lastCursor = cursor;
        var feet = cursor - _grabOffset;
        _brain!.DragTo(feet.X, Mathf.Min(feet.Y, _windowSize.Y - 1f));
    }

    private void UpdateHitRegion()
    {
        if (_dragging || _hitShape == null)
            return;

        var tight = _hitShape.Outline(_camera, HitMargin);
        if (_region.Length >= 3 && Array.TrueForAll(tight, p => Geometry2D.IsPointInPolygon(p, _region)))
            return;

        // A little slack so a breathing or walking character does not need a new
        // window region (an expensive call on Windows) every frame.
        float slack = _brain!.State == MascotState.Falling ? 0f : HitSlack;
        SetMousePassthrough(_hitShape.Outline(_camera, HitMargin + slack));
    }

    private void SetMousePassthrough(Vector2[] region)
    {
        _region = region;
        DisplayServer.WindowSetMousePassthrough(region);
    }

    private Vector3 ScreenToWorld(Vector2 p) =>
        new((p.X - _windowSize.X * 0.5f) / _pixelsPerMetre, (_windowSize.Y * 0.5f - p.Y) / _pixelsPerMetre, 0f);

    private Vector2 CursorInWindow() => DisplayServer.MouseGetPosition() - GetWindow().Position;

    private void BuildMenu()
    {
        _menu.Clear();
        _menu.AddItem(Tr("MENU_BODY_EDITOR"), MenuCreator);
        _menu.AddSeparator();
        _menu.AddItem(Tr("MENU_CALL_OVER"), MenuCallOver);
        _menu.AddItem(Tr("MENU_USE_BODY"), MenuUseBody);
        _menu.AddItem(Tr("MENU_LOAD_CHARACTER"), MenuLoad);
        _menu.AddItem(Tr("MENU_DEFAULT_CHARACTER"), MenuReset);
        _menu.AddSeparator();
        _menu.AddItem(Tr("MENU_QUIT"), MenuQuit);
        _menu.IdPressed += OnMenu;
        _menu.AboutToPopup += () =>
            _menu.SetItemDisabled(_menu.GetItemIndex(MenuUseBody), !FileAccess.FileExists(CreatorRoot.SavePath));

        _fileDialog.Title = Tr("DIALOG_OPEN_CHARACTER");
        _fileDialog.Filters = new[] { "*.vrm ; VRM" };
    }

    private void ShowMenu(Vector2I screenPosition)
    {
        _menu.Position = screenPosition;
        _menu.Popup();
    }

    private void OnMenu(long id)
    {
        switch (id)
        {
            case MenuCallOver:
                _brain?.WalkTo(CursorInWindow().X);
                break;
            case MenuLoad:
                _fileDialog.Popup();
                break;
            case MenuReset:
                if (LoadCharacter(DefaultCharacter, persist: false))
                    SettingsStore.UseCharacter(CharacterSource.Default);
                break;
            case MenuUseBody:
                if (LoadBody())
                    SettingsStore.UseCharacter(CharacterSource.Body);
                break;
            case MenuQuit:
                GetTree().Quit();
                break;
            case MenuCreator:
                AppWindow.OpenCreator(this);
                break;
        }
    }

    private void OnTrayPressed(long mouseButton, Vector2I position)
    {
        if (mouseButton == (long)MouseButton.Left)
            _brain?.WalkTo(_windowSize.X * 0.5f);
    }

    /// <summary>The current mouse region outline (for snapshots).</summary>
    internal Vector2[] HitOutline() => _hitShape?.Outline(_camera, HitMargin) ?? Array.Empty<Vector2>();

    internal Image CaptureFrame() => GetViewport().GetTexture().GetImage();
}
