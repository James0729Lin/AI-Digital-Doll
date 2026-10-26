using System;
using System.Collections.Generic;
using System.Linq;
using DigitalDoll.Body;
using DigitalDoll.Character;
using DigitalDoll.Chat;
using DigitalDoll.Core;
using DigitalDoll.Core.Acting;
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
/// On Windows the whole window is switched between taking the mouse and letting it
/// through (a layered, click-through window) as the cursor moves over the character
/// or away. Shaping the window to the character instead (Godot's mouse passthrough
/// polygon) made Windows show stale, never-drawn pixels for a frame wherever the shape
/// grew, so a moving character flickered with odd colours around it.
/// </summary>
public partial class MascotRoot : Node3D
{
    private const string DefaultCharacter = "res://assets/characters/godette/Godette_vrm_v4.vrm";
    private const string SavedCharacter = "user://characters/current.vrm";
    private const float CharacterHeightPoints = 300f;
    private const float HitMargin = 5f;
    private const float HitSlack = 6f;
    private const float LeadSeconds = 0.1f;

    /// <summary>Cursor travel (px) or hold time (s) that turns a press into picking the character up.</summary>
    private const float DragThreshold = 6f;
    private const float HoldToDrag = 0.35f;
    private const float DoubleClickWait = 0.28f;

    private const int MenuCallOver = 0;
    private const int MenuLoad = 1;
    private const int MenuReset = 2;
    private const int MenuQuit = 3;
    private const int MenuCreator = 4;
    private const int MenuUseBody = 5;
    private const int MenuStay = 6;
    private const int MenuChat = 7;
    private const int MenuAiSettings = 8;

    /// <summary>
    /// The body editor and the edited body are hidden for now (the code is kept, and
    /// <c>--creator</c> still opens the editor for development); the Windows export
    /// leaves out their assets. Set to true to bring both back to the menu.
    /// </summary>
    private static readonly bool BodyEditorEnabled = false;

    private Camera3D _camera = null!;
    private Node3D _pivot = null!;
    private PopupMenu _menu = null!;
    private FileDialog _fileDialog = null!;

    private readonly Random _random = new();
    private MascotBrain? _brain;
    private Node3D? _model;
    private IMascotBody? _body;
    private IPerformer? _animator;

    private Vector2I _windowSize;
    private float _pixelsPerMetre = 180f;
    private float _heightPixels = CharacterHeightPoints;
    private float _baseHeightPixels = CharacterHeightPoints;
    private float _modelHeight = 1.6f;
    private float _yaw;
    private float _screenCheck;
    private float _windowsCheck;
    private Vector2[] _region = Array.Empty<Vector2>();
    private bool? _clickThrough;
    private float _wallShift;

    private bool _dragging;
    private bool _pressed;
    private Vector2 _pressCursor;
    private float _pressTime;
    private float _pokeDelay = -1f;
    private Vector2 _pokeCursor;
    private Vector2 _lastHover = new(float.NaN, float.NaN);
    private float _nearTravel;
    private Vector2[] _tight = Array.Empty<Vector2>();
    private TouchTracker _touch = null!;
    private PerformanceDirector _director = null!;
    private Vector2 _grabOffset;
    private Vector2 _lastCursor;
    private Vector2 _dragVelocity;

    private Snapshot? _snapshot;
    private ChatController? _chat;

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

        _touch = new TouchTracker(_random);
        _director = new PerformanceDirector(_random);
        _snapshot?.Begin(_brain!, _windowSize);
        if (_snapshot != null)
        {
            _snapshot.Act(_animator!);
        }
        else
        {
            _chat = new ChatController();
            AddChild(_chat);
            _chat.Attach(this);
            DoubleClicked += _chat.OpenChat;
        }
        SelfTest.OnSceneReady(this, "desktop");
    }

    public override void _Process(double delta)
    {
        float dt = _snapshot != null ? 1f / 60f : (float)delta;
        if (_brain == null || _body == null || _animator == null)
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

        if (_pressed && !_dragging)
        {
            _pressTime += dt;
            if (CursorInWindow().DistanceTo(_pressCursor) > DragThreshold || _pressTime > HoldToDrag)
                StartDrag();
        }
        if (_dragging)
            UpdateDrag(dt);
        if (_pokeDelay >= 0f)
        {
            _pokeDelay -= dt;
            if (_pokeDelay < 0f)
                Poke(_pokeCursor);
        }
        if (_snapshot == null)
            UpdateTouch(dt);

        _brain.Update(dt);
        PlaceCharacter(dt);

        _animator.LookYaw = 0f;
        _animator.LookPitch = 0f;
        if (_brain.State == MascotState.Idle && _snapshot == null && !FaceUser)
            AimHeadAtCursor();
        _animator.Update(dt, _brain.State, _brain.VelocityX / _pixelsPerMetre, _dragVelocity.X / _pixelsPerMetre,
            _brain.State == MascotState.Climb ? _brain.VelocityY / _pixelsPerMetre : 0f, _brain.Running, _brain.ControlledFall, _brain.Grip);

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
            if (button.ButtonIndex == MouseButton.Left && button.Pressed && button.DoubleClick)
            {
                // A double click talks to the character instead of poking it twice.
                _pokeDelay = -1f;
                _pressed = false;
                DoubleClicked?.Invoke();
            }
            else if (button.ButtonIndex == MouseButton.Left && button.Pressed)
            {
                // Picked up only once the cursor moves or the button is held; a quick
                // click is a poke.
                _pressed = true;
                _pressTime = 0f;
                _pressCursor = CursorInWindow();
            }
            else if (button.ButtonIndex == MouseButton.Left && !button.Pressed)
            {
                if (_dragging)
                {
                    _dragging = false;
                    _brain.EndDrag(_dragVelocity.X, Mathf.Min(_dragVelocity.Y, 0f));
                }
                else if (_pressed)
                {
                    _pokeDelay = DoubleClickWait;
                    _pokeCursor = _pressCursor;
                }
                _pressed = false;
            }
            else if (button.ButtonIndex == MouseButton.Right && button.Pressed)
            {
                ShowMenu(DisplayServer.MouseGetPosition());
            }
        }
    }

    /// <summary>Raised when the character is double-clicked (opens the chat).</summary>
    public event Action? DoubleClicked;

    internal MascotBrain? Brain => _brain;
    internal float CharacterHeightPixels => _heightPixels;
    internal IPerformer? Animator => _animator;
    internal IMascotBody? Body => _body;

    /// <summary>Screen position just above the character's head (for the speech bubble).</summary>
    internal Vector2I HeadScreenPosition()
    {
        if (_body == null)
            return GetWindow().Position;
        var head = _camera.UnprojectPosition(_body.HeadPosition);
        float top = _tight.Length > 0 ? _tight.Min(p => p.Y) : head.Y - _heightPixels * 0.12f;
        return GetWindow().Position + new Vector2I((int)head.X, (int)Mathf.Min(top, head.Y));
    }

    /// <summary>Performs an action asked for in the chat ("sit", "lie" or a performance action).</summary>
    internal void PerformCommand(string action)
    {
        if (_brain == null || _animator == null)
            return;
        switch (action)
        {
            case "sit":
                _brain.SitDown();
                break;
            case "lie":
                _brain.LieDown();
                break;
            default:
                if (_director.Pick(action, 0.8f) is GestureCue cue)
                    _animator.Play(cue, now: true);
                break;
        }
    }

    private void StartDrag()
    {
        _dragging = true;
        _lastCursor = CursorInWindow();
        _grabOffset = _pressCursor - new Vector2(_brain!.X, _brain.Y);
        _dragVelocity = Vector2.Zero;
        _brain.BeginDrag();
        _animator?.StopGestures();
        if (_clickThrough == null)
            SetMousePassthrough(Array.Empty<Vector2>());
    }

    private TouchZone ZoneAt(Vector2 cursor)
    {
        if (_body == null)
            return TouchZone.Body;
        float neck = _camera.UnprojectPosition(_body.NeckPosition).Y;
        return cursor.Y < neck ? TouchZone.Head : TouchZone.Body;
    }

    private void Poke(Vector2 cursor)
    {
        var brain = _brain!;
        if (brain.State == MascotState.Lie)
        {
            // Woken with a start.
            brain.Wake();
            _animator!.Feel(Emotion.Surprised, 0.9f, 2f);
            return;
        }
        if (brain.State is MascotState.Sprawl or MascotState.Tumble)
        {
            _animator!.Feel(Emotion.Sad, 0.9f, 2f);
            return;
        }
        React(_touch.Poke(ZoneAt(cursor)), cursor);
    }

    private void React(TouchReaction reaction, Vector2 cursor)
    {
        var animator = _animator!;
        animator.Feel(reaction.Emotion, reaction.Intensity, reaction.Flee ? 5f : 2.5f);
        if (reaction.Gesture is Gesture gesture)
            animator.Play(_director.Vary(gesture, reaction.Intensity), now: true);
        if (reaction.Flee)
            _brain!.Flee(cursor.X, cursor.Y);
        else
            _brain!.Hold(2.5f);
    }

    /// <summary>Petting and rubbing: the cursor moving back and forth over the character.</summary>
    private void UpdateTouch(float dt)
    {
        var brain = _brain!;
        var cursor = CursorInWindow();
        bool over = !_pressed && !_dragging && _tight.Length >= 3 && Geometry2D.IsPointInPolygon(cursor, _tight);
        float travel = 0f;
        if (!float.IsNaN(_lastHover.X))
            travel = cursor.DistanceTo(_lastHover) / _heightPixels;
        _lastHover = cursor;

        var zone = ZoneAt(cursor);
        if (_touch.Update(dt, over ? travel : 0f, zone) is TouchReaction reaction)
        {
            if (brain.State == MascotState.Lie)
                brain.Wake();
            React(reaction, cursor);
        }
        _animator!.Petted = _touch.Petting;
        _animator.PetZone = _touch.Zone;
        if (_touch.Petting)
            brain.Hold(1f);

        // Moving the mouse around close to a sleeping character wakes it.
        if (brain.State == MascotState.Lie)
        {
            var centre = new Vector2(brain.X, brain.Y - _heightPixels * 0.12f);
            _nearTravel = cursor.DistanceTo(centre) < _heightPixels * 0.7f ? _nearTravel + travel : _nearTravel * MathF.Exp(-dt);
            if (_nearTravel > 0.6f)
            {
                _nearTravel = 0f;
                brain.Wake();
                _animator.Feel(Emotion.Surprised, 0.6f, 1.5f);
                _animator.Play(_director.Vary(Gesture.Yawn, 0.6f));
            }
        }
        else
        {
            _nearTravel = 0f;
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
        _baseHeightPixels = CharacterHeightPoints * Mathf.Clamp(scale, 1f, 3f);
        _heightPixels = _baseHeightPixels * (_body?.SizeFactor ?? 1f);
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
        _baseHeightPixels = snapshot.WindowSize.Y * 0.8f;
        _heightPixels = _baseHeightPixels;
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
        if (_body == null)
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
            if (_snapshot.Animal is Species animal)
                return LoadAnimal(animal, _snapshot.AnimalStyle);
            return _snapshot.Body != null
                ? AttachCharacter(BodyCharacter.Create(_snapshot.Body), "body (snapshot)")
                : LoadCharacter(_snapshot.CharacterPath ?? DefaultCharacter, persist: false);
        }

        var settings = SettingsStore.Load();
        return settings.Character switch
        {
            // A saved edited body falls back to the default character while the editor is hidden.
            CharacterSource.Body when BodyEditorEnabled => LoadBody(),
            CharacterSource.Animal => LoadAnimal(settings.Animal, settings.AnimalStyle),
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

        var expressions = ExpressionRig.Create(model);
        var animator = new ProceduralAnimator(rig, expressions, _random);
        // VRM models carry spring bone settings on their root (vrm_toplevel.gd).
        if (model.Get("springbone_add_force").VariantType == Variant.Type.Vector3)
            animator.Springs = model;
        GD.Print($"Loaded {label}: expressions [{string.Join(", ", expressions.Names)}]");

        // Turn the model so that its forward axis matches the pivot's +Z (toward the camera).
        var forward = model.GlobalBasis.Inverse() * (rig.Skeleton.GlobalBasis * rig.Forward);
        model.Rotation = new Vector3(0f, -Mathf.Atan2(forward.X, forward.Z), 0f);

        Attach(new HumanBody(model, rig, animator, CharacterHitShape.Measure(model, rig.Skeleton)));
        return true;
    }

    /// <summary>Shows an animal instead of the doll.</summary>
    /// <summary>
    /// Shows an animal in one of its versions (1.0 built from shapes, 2.0 and 3.0 from
    /// bundled models); a version the species does not come in falls back to 1.0.
    /// </summary>
    private bool LoadAnimal(Species species, AnimalStyle style)
    {
        IMascotBody animal = (style == AnimalStyle.Classic ? null : Animals.ModelAnimalBody.Create(species, style, _random))
            ?? (IMascotBody)Animals.AnimalBody.Create(species, _random);
        _pivot.AddChild(animal.Model);
        Attach(animal);
        return true;
    }

    /// <summary>Menu ids for the animals: species and version packed together.</summary>
    private static int AnimalMenuId(Species species, AnimalStyle style) => (int)species * 10 + (int)style;

    private void Attach(IMascotBody body)
    {
        _model?.QueueFree();
        _model = body.Model;
        _body = body;
        _animator = body.Performer;
        _region = Array.Empty<Vector2>();

        float previous = _heightPixels;
        // Snapshots frame any character at the same size.
        _heightPixels = _snapshot != null ? _baseHeightPixels : _baseHeightPixels * body.SizeFactor;
        // Model animals know their height (their skinned meshes' own boxes ignore the skeleton).
        _modelHeight = body is Animals.ModelAnimalBody model ? model.Size.Y : MeasureHeight(body.Model, _pivot);
        UpdateCamera();

        // A different size walks, jumps and climbs differently: start a new brain where
        // the old one stood.
        if (_brain == null || !Mathf.IsEqualApprox(previous, _heightPixels))
        {
            float x = _brain?.X ?? _windowSize.X * 0.7f;
            bool staying = _brain?.Staying ?? false;
            _brain = new MascotBrain(
                new MascotTuning
                {
                    WalkSpeed = body.WalkSpeed * _pixelsPerMetre,
                    ClimbSpeed = body.ClimbSpeed * _pixelsPerMetre,
                    Height = _heightPixels,
                },
                _random, x, BuildSurroundings()) { Staying = staying };
        }
        _chat?.CharacterChanged(body.Name, body.Persona);
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
        float x = _snapshot != null ? _windowSize.X * 0.5f : brain.X + UpdateWallShift(dt);
        _pivot.Position = ScreenToWorld(new Vector2(x, brain.Y));

        float target = _body!.Yaw(brain.State, brain.Facing, brain.WallSide, Facing0);
        if (_snapshot?.Yaw is float yaw)
            target = yaw;
        _yaw = Mathf.Lerp(_yaw, target, 1f - MathF.Exp(-8f * dt));
        _pivot.Rotation = new Vector3(0f, _yaw, 0f);
    }

    /// <summary>
    /// While climbing, how far the character is drawn away from its wall: knees and
    /// hands reach toward the wall by different amounts on every model and in every
    /// pose, so the drawn outline is measured and the character eased off the wall until
    /// nothing pokes past it (past a screen edge it would be cut off).
    /// </summary>
    private float UpdateWallShift(float dt)
    {
        var brain = _brain!;
        float target = 0f;
        if (brain.State is MascotState.Climb or MascotState.Cling && brain.WallId is long id
            && brain.Surroundings.FindWall(id) is Wall wall && _tight.Length >= 3)
        {
            float extreme = wall.Side > 0 ? _tight.Max(p => p.X) : _tight.Min(p => p.X);
            // A few pixels' gap so a reaching limb does not touch the edge.
            float overshoot = (extreme - wall.X) * wall.Side - HitMargin + 6f;
            // Only ever away from the wall (never pulled into it).
            target = _wallShift - wall.Side * overshoot;
            target = wall.Side > 0 ? MathF.Min(target, 0f) : MathF.Max(target, 0f);
        }
        // Quick to move off the wall, slow to drift back, so the heaving limbs do not
        // make the character rock toward and away from it.
        bool away = MathF.Abs(target) > MathF.Abs(_wallShift);
        _wallShift = Mathf.Lerp(_wallShift, target, 1f - MathF.Exp(-(away ? 14f : 0.8f) * dt));
        return _wallShift;
    }

    /// <summary>Set while the character should face the user (talking, being petted).</summary>
    internal bool FaceUser { get; set; }

    private bool Facing0 => FaceUser || _touch.Petting;

    private void AimHeadAtCursor()
    {
        _body!.LookAt(ScreenToWorld(CursorInWindow()) + new Vector3(0f, 0f, 0.6f));
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
        if (_body == null)
            return;
        if (!_dragging)
            _tight = _body.Outline(_camera, HitMargin);
        // Windows: take the mouse while it is over the character (or holding it), let it
        // through everywhere else; the window keeps its full shape, so nothing flickers.
        var cursor = CursorInWindow();
        bool over = _dragging || _pressed || (_tight.Length >= 3 && Geometry2D.IsPointInPolygon(cursor, _tight));
        if (_clickThrough != !over && WindowsNative.SetClickThrough(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle), !over))
            _clickThrough = !over;
        if (_clickThrough != null || _dragging)
            return;

        // Elsewhere (the Linux smoke test): shape the window's mouse region to the character.
        if (_region.Length >= 3 && Array.TrueForAll(_tight, p => Geometry2D.IsPointInPolygon(p, _region)))
            return;
        // Slack so a breathing or walking character does not need a new window region
        // every frame; a fast fall or jump needs room for ~0.1 s of travel.
        var brain = _brain!;
        float speed = new Vector2(brain.VelocityX, brain.VelocityY).Length();
        float slack = HitSlack + speed * LeadSeconds;
        SetMousePassthrough(_body.Outline(_camera, HitMargin + slack));
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
        _menu.AddItem(Tr("MENU_CHAT"), MenuChat);
        _menu.AddItem(Tr("MENU_AI_SETTINGS"), MenuAiSettings);
        _menu.AddSeparator();
        if (BodyEditorEnabled)
        {
            _menu.AddItem(Tr("MENU_BODY_EDITOR"), MenuCreator);
            _menu.AddSeparator();
        }
        _menu.AddItem(Tr("MENU_CALL_OVER"), MenuCallOver);
        _menu.AddCheckItem(Tr("MENU_STAY"), MenuStay);
        if (BodyEditorEnabled)
            _menu.AddItem(Tr("MENU_USE_BODY"), MenuUseBody);
        var animals = new PopupMenu { Name = "Animals" };
        // Each animal in every version it comes in: "Cat 1.0", "Cat 2.0", "Cat 3.0"…
        foreach (var species in Enum.GetValues<Species>())
        {
            foreach (var style in Enum.GetValues<AnimalStyle>())
            {
                if (style == AnimalStyle.Classic || Animals.ModelAnimalBody.Has(species, style))
                    animals.AddItem($"{Tr("ANIMAL_" + species.ToString().ToUpperInvariant())} {(int)style + 1}.0", AnimalMenuId(species, style));
            }
        }
        animals.IdPressed += id =>
        {
            var species = (Species)((int)id / 10);
            var style = (AnimalStyle)((int)id % 10);
            if (LoadAnimal(species, style))
                SettingsStore.UseAnimal(species, style);
        };
        _menu.AddSubmenuNodeItem(Tr("MENU_ANIMALS"), animals);
        _menu.AddItem(Tr("MENU_LOAD_CHARACTER"), MenuLoad);
        _menu.AddItem(Tr("MENU_DEFAULT_CHARACTER"), MenuReset);
        _menu.AddSeparator();
        _menu.AddItem(Tr("MENU_QUIT"), MenuQuit);
        _menu.IdPressed += OnMenu;
        if (BodyEditorEnabled)
        {
            _menu.AboutToPopup += () =>
                _menu.SetItemDisabled(_menu.GetItemIndex(MenuUseBody), !FileAccess.FileExists(CreatorRoot.SavePath));
        }

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
            case MenuChat:
                _chat?.OpenChat();
                break;
            case MenuAiSettings:
                _chat?.OpenSettings();
                break;
            case MenuQuit:
                GetTree().Quit();
                break;
            case MenuCreator:
                AppWindow.OpenCreator(this);
                break;
            case MenuStay:
                if (_brain != null)
                {
                    _brain.Staying = !_brain.Staying;
                    _menu.SetItemChecked(_menu.GetItemIndex(MenuStay), _brain.Staying);
                }
                break;
        }
    }

    private void OnTrayPressed(long mouseButton, Vector2I position)
    {
        if (mouseButton == (long)MouseButton.Left)
            _brain?.WalkTo(_windowSize.X * 0.5f);
    }

    /// <summary>The current mouse region outline (for snapshots).</summary>
    internal Vector2[] HitOutline() => _body?.Outline(_camera, HitMargin) ?? Array.Empty<Vector2>();

    internal Image CaptureFrame() => GetViewport().GetTexture().GetImage();
}
