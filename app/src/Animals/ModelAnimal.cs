using System;
using System.Collections.Generic;
using System.Linq;
using DigitalDoll.Character;
using DigitalDoll.Core;
using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Mascot;
using Godot;

namespace DigitalDoll.Animals;

/// <summary>
/// An animal from a ready-made animated model (Kenney's Cube Pets, Quaternius' low-poly
/// animals): the model's own clips play for walking, idling, dancing and gestures, and
/// the moves it has no clip for (climbing, lying, being carried, falling…) come from
/// tilting and squashing the whole model.
/// </summary>
public sealed class ModelAnimalBody : IMascotBody
{
    /// <summary>
    /// A bundled model: its file, a size correction, the turn that makes it face +Z, and
    /// colours for materials by name (the low-poly pack's files keep the material names
    /// but not their colours).
    /// </summary>
    private sealed record Spec(string Path, float Scale = 1f, float Turn = 0f, Dictionary<string, Color>? Colours = null);

    private static readonly Dictionary<(Species, AnimalStyle), Spec> Specs = new()
    {
        [(Species.Cat, AnimalStyle.Cube)] = new("res://assets/animals/cube/animal-cat.glb"),
        [(Species.Dog, AnimalStyle.Cube)] = new("res://assets/animals/cube/animal-dog.glb"),
        [(Species.Chicken, AnimalStyle.Cube)] = new("res://assets/animals/cube/animal-chick.glb"),
        [(Species.Pig, AnimalStyle.Cube)] = new("res://assets/animals/cube/animal-pig.glb"),
        // A black cat with a white chest and paws and pink ears, a grey dog, as in the pack's preview.
        [(Species.Cat, AnimalStyle.LowPoly)] = new("res://assets/animals/lowpoly/Cat.fbx", Turn: -Mathf.Pi / 2f, Colours: new()
        {
            ["grey"] = new Color(0.22f, 0.22f, 0.25f),
            ["pink"] = new Color(0.96f, 0.62f, 0.74f),
            ["white"] = new Color(0.96f, 0.95f, 0.93f),
        }),
        [(Species.Dog, AnimalStyle.LowPoly)] = new("res://assets/animals/lowpoly/Dog.fbx", Turn: -Mathf.Pi / 2f, Colours: new()
        {
            ["dog"] = new Color(0.55f, 0.54f, 0.52f),
        }),
    };

    /// <summary>Height of every model animal in metres (about the classic animals' size).</summary>
    private const float TargetHeight = 0.3f;

    private readonly ModelAnimalPerformer _performer;
    private readonly Node3D _model;
    private readonly List<MeshInstance3D> _meshes;
    private readonly List<Vector2> _points = new();

    private ModelAnimalBody(Species species, Node3D model, Node3D root, Node3D body, AnimationPlayer? player, Aabb box, Random random)
    {
        Species = species;
        _model = model;
        _meshes = model.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToList();
        Size = box.Size;
        _performer = new ModelAnimalPerformer(species, root, body, player, box.Size, random);
    }

    /// <summary>Whether this species comes in this version.</summary>
    public static bool Has(Species species, AnimalStyle style) => Specs.ContainsKey((species, style));

    public static ModelAnimalBody? Create(Species species, AnimalStyle style, Random random)
    {
        if (!Specs.TryGetValue((species, style), out var spec) || GD.Load<PackedScene>(spec.Path) is not PackedScene scene)
            return null;
        var instance = scene.Instantiate<Node3D>();
        var model = new Node3D { Name = species.ToString() };
        var root = new Node3D { Name = "Root" };
        var body = new Node3D { Name = "Body" };
        model.AddChild(root);
        root.AddChild(body);
        body.AddChild(instance);

        if (spec.Colours != null)
            Recolour(instance, spec.Colours);

        // Normalise: face +Z, feet on the ground, centred, TargetHeight tall.
        instance.Rotation = new Vector3(0f, spec.Turn, 0f);
        var box = Measure(instance);
        float scale = TargetHeight / Mathf.Max(box.Size.Y, 0.001f) * spec.Scale;
        instance.Scale = Vector3.One * scale;
        var centre = box.GetCenter();
        instance.Position = new Vector3(-centre.X, -box.Position.Y, -centre.Z) * scale;
        var size = box.Size * scale;

        var player = instance.FindChildren("*", "AnimationPlayer", true, false).Cast<AnimationPlayer>().FirstOrDefault();
        return new ModelAnimalBody(species, model, root, body, player, new Aabb(new Vector3(-size.X / 2f, 0f, -size.Z / 2f), size), random);
    }

    public Species Species { get; }
    public Vector3 Size { get; }
    public Node3D Model => _model;
    public IPerformer Performer => _performer;
    public float SizeFactor => Species == Species.Chicken ? 0.42f : 0.48f;
    public float WalkSpeed => TargetHeight * 0.85f;
    public float ClimbSpeed => TargetHeight * 0.55f;
    public string? Persona => Creatures.Persona(Species);
    public string? Name => Creatures.DefaultName(Species, TranslationServer.GetLocale().StartsWith("zh"));

    // The models have no head bone to speak of: the top front of the body stands in.
    public Vector3 HeadPosition => _performer.Body.ToGlobal(new Vector3(0f, Size.Y * 0.85f, Size.Z * 0.25f));
    public Vector3 NeckPosition => _performer.Body.ToGlobal(new Vector3(0f, Size.Y * 0.55f, Size.Z * 0.25f));

    public void LookAt(Vector3 target)
    {
        var local = _model.GlobalBasis.Inverse() * (target - HeadPosition);
        _performer.LookYaw = Mathf.Atan2(local.X, Mathf.Max(local.Z, 0.05f));
        _performer.LookPitch = Mathf.Atan2(local.Y, new Vector2(local.X, local.Z).Length());
    }

    public float Yaw(MascotState state, int facing, int wallSide, bool faceUser) => state switch
    {
        // Walking mostly side-on, turned a little toward the viewer so the face shows.
        MascotState.Walk => facing * 1.15f,
        MascotState.Sprawl => facing * Mathf.Pi / 2f,
        MascotState.Falling or MascotState.Jump => facing * Mathf.Pi / 2f * 0.8f,
        MascotState.Climb or MascotState.Cling => wallSide * Mathf.Pi / 2f,
        MascotState.Lie or MascotState.Landing or MascotState.GetUp => facing * 1.0f,
        MascotState.Sit or MascotState.FloorSit or MascotState.Tumble => facing * 0.5f,
        MascotState.Dragged => facing * 0.4f,
        MascotState.Dance => 0f,
        // Side-on enough for what it is doing in front of it (a bowl, a ball) to show.
        MascotState.Pose => facing * 0.95f,
        _ when faceUser => facing * 0.35f,
        _ => facing * 0.7f,
    };

    public Vector2[] Outline(Camera3D camera, float margin)
    {
        // The posed model's box, corner by corner: the models are compact and boxy, so
        // their convex hull fits well enough for a mouse region.
        _points.Clear();
        var box = new Aabb(new Vector3(-Size.X / 2f, 0f, -Size.Z / 2f), Size);
        for (int i = 0; i < 8; i++)
            _points.Add(camera.UnprojectPosition(_performer.Body.ToGlobal(box.GetEndpoint(i))));
        return CharacterHitShape.Grow(Geometry2D.ConvexHull(_points.ToArray()), margin);
    }

    private static void Recolour(Node3D root, Dictionary<string, Color> colours)
    {
        foreach (var mesh in root.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            for (int i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
            {
                string name = (mesh.Mesh?.SurfaceGetMaterial(i)?.ResourceName ?? "").ToLowerInvariant();
                foreach (var (key, colour) in colours)
                {
                    if (name.Contains(key))
                        mesh.SetSurfaceOverrideMaterial(i, new StandardMaterial3D { AlbedoColor = colour, Roughness = 1f });
                }
            }
        }
    }

    /// <summary>
    /// The visible meshes' box in <paramref name="root"/>'s parent space. Skinned meshes are
    /// measured vertex by vertex in their rest pose, since their own box ignores the
    /// skeleton (often scaled by the exporter).
    /// </summary>
    private static Aabb Measure(Node3D root)
    {
        Aabb? box = null;
        foreach (var mesh in root.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            var skin = mesh.Skin;
            var skeleton = skin != null ? mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton) : null;
            if (skin != null && skeleton != null && mesh.Mesh is Mesh skinned)
            {
                var toRoot = ToRoot(skeleton, root);
                var binds = new Transform3D[skin.GetBindCount()];
                for (int b = 0; b < binds.Length; b++)
                {
                    int bone = skin.GetBindBone(b);
                    if (bone < 0)
                        bone = skeleton.FindBone(skin.GetBindName(b));
                    binds[b] = toRoot * (bone >= 0 ? skeleton.GetBoneGlobalRest(bone) : Transform3D.Identity) * skin.GetBindPose(b);
                }
                for (int surface = 0; surface < skinned.GetSurfaceCount(); surface++)
                {
                    var arrays = skinned.SurfaceGetArrays(surface);
                    var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                    var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                    int per = vertices.Length > 0 ? bones.Length / vertices.Length : 0;
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        var at = Vector3.Zero;
                        float total = 0f;
                        for (int k = 0; k < per; k++)
                        {
                            float w = weights[v * per + k];
                            int bind = bones[v * per + k];
                            if (w <= 0f || bind < 0 || bind >= binds.Length)
                                continue;
                            at += binds[bind] * vertices[v] * w;
                            total += w;
                        }
                        if (total <= 0f)
                            continue;
                        at /= total;
                        box = box?.Expand(at) ?? new Aabb(at, Vector3.Zero);
                    }
                }
                continue;
            }
            var b2 = ToRoot(mesh, root) * mesh.GetAabb();
            box = box?.Merge(b2) ?? b2;
        }
        return box ?? new Aabb(Vector3.Zero, Vector3.One);
    }

    /// <summary>A node's transform in <paramref name="root"/>'s parent space (the model is not in a tree yet).</summary>
    private static Transform3D ToRoot(Node3D node, Node3D root)
    {
        var transform = node.Transform;
        for (var parent = node.GetParent() as Node3D; parent != null && parent != root.GetParent(); parent = parent.GetParent() as Node3D)
            transform = parent.Transform * transform;
        return transform;
    }
}

/// <summary>Plays a model animal's clips for each behaviour and fills the gaps by posing the whole model.</summary>
public sealed partial class ModelAnimalPerformer : IPerformer
{
    private const float WeightRate = 9f;

    private readonly Node3D _root;
    private readonly AnimationPlayer? _player;
    private readonly Vector3 _size;
    private readonly Random _random;
    private readonly Dictionary<string, string> _clips = new();

    private string? _playing;
    private float _time, _stateTime, _gestureTime, _fidgetTimer = 6f;
    private MascotState _state;
    private GestureCue? _gesture;
    private Emotion _emotion;
    private float _emotionHold;
    private int _lieSide = 1;

    // Behaviour weights for the whole-model poses.
    private float _climb, _lie, _sprawl, _tumble, _sit, _fall, _drag, _land, _slip, _hang, _getUp, _jump;
    private float _dangle, _dangleVelocity, _lookYaw;

    // Activities: the props, and the front legs posed over the clips (batting a ball,
    // washing the face) where the model has them as nodes of their own.
    private readonly ActivityProps _props;
    private readonly Node3D?[] _frontLegs = new Node3D?[2];
    private readonly Node3D?[] _backLegs = new Node3D?[2];
    private readonly Dictionary<Node3D, (Transform3D Base, Transform3D Written)> _legs = new();
    private string? _activity, _poseId;
    private float _act, _stepPhase;

    public ModelAnimalPerformer(Species species, Node3D root, Node3D body, AnimationPlayer? player, Vector3 size, Random random)
    {
        _root = root;
        Body = body;
        _player = player;
        _size = size;
        _random = random;
        _props = new ActivityProps(root.GetParent<Node3D>(), species, size.Y, size.Z);
        _rig = ModelAnimalRig.TryCreate(body, root.GetParent<Node3D>());
        foreach (var node in body.FindChildren("*leg*", "Node3D", true, false).Cast<Node3D>())
        {
            string name = node.Name.ToString().ToLowerInvariant();
            int side = name.Contains("left") ? 0 : name.Contains("right") ? 1 : -1;
            if (side < 0)
                continue;
            if (name.Contains("front"))
                _frontLegs[side] ??= node;
            else if (name.Contains("back"))
                _backLegs[side] ??= node;
        }
        if (player == null)
            return;
        // Advanced by hand, so the legs can be posed over the clip afterwards.
        player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        // Clip names differ by pack ("walk", "CatArmature|Walking"…): find each by keyword.
        foreach (string name in player.GetAnimationList())
        {
            string key = name.ToLowerInvariant();
            foreach (var (role, words) in Roles)
            {
                if (!_clips.ContainsKey(role) && words.Any(key.Contains))
                    _clips[role] = name;
            }
        }
        foreach (string role in new[] { "idle", "walk", "run", "dance", "static" })
        {
            if (_clips.TryGetValue(role, out var clip) && player.GetAnimation(clip) is Animation animation)
                animation.LoopMode = Animation.LoopModeEnum.Linear;
        }
    }

    private static readonly (string Role, string[] Words)[] Roles =
    {
        ("idle", new[] { "idle" }),
        ("walk", new[] { "walk" }),
        ("run", new[] { "run", "gallop" }),
        ("dance", new[] { "dance" }),
        ("eat", new[] { "eat" }),
        ("yes", new[] { "gesture-positive", "positive", "yes" }),
        ("no", new[] { "gesture-negative", "negative", "no" }),
        ("jump", new[] { "jump" }),
        ("static", new[] { "static", "t-pose", "tpose" }),
    };

    /// <summary>The node holding the model (posed as a whole).</summary>
    public Node3D Body { get; }

    public float LookYaw { get; set; }
    public float LookPitch { get; set; }
    public bool Fidgets { get; set; } = true;
    public bool Talking { get; set; }
    public bool Petted { get; set; }
    public TouchZone PetZone { get; set; }
    public Gesture? CurrentGesture => _gesture?.Gesture;

    public void Play(GestureCue cue, bool now = false)
    {
        if (_gesture != null && !now)
            return;
        _gesture = cue;
        _gestureTime = 0f;
    }

    public void StopGestures() => _gesture = null;

    /// <summary>
    /// An activity (see <see cref="ActivityCatalog"/>) is done for as long as the pose is
    /// held; the models have no hands for the doll's poses, so those get a happy wriggle.
    /// </summary>
    public void StrikePose(string id)
    {
        if (id == _poseId)
            return;
        _poseId = id;
        if (ActivityCatalog.Has(id))
            _activity = id;
        else
            Play(new GestureCue(Gesture.Nod, 1.6f, 0.8f, false), now: true);
    }

    public void Feel(Emotion emotion, float intensity, float seconds = 4f)
    {
        _emotion = emotion;
        _emotionHold = emotion == Emotion.Neutral ? 0f : seconds;
    }

    public void Update(float dt, MascotState state, float speedMetres, float dragVelocityMetres, float climbSpeedMetres = 0f, bool running = false,
        bool bracing = false, ClimbGrip grip = ClimbGrip.Steady)
    {
        _time += dt;
        _stateTime = state == _state ? _stateTime + dt : 0f;
        if (state == MascotState.Lie && _state != MascotState.Lie)
            _lieSide = _random.Next(2) == 0 ? -1 : 1;
        _state = state;
        if (state != MascotState.Pose)
        {
            _fadingActivity = _activity ?? _fadingActivity;
            _activity = _poseId = null;
        }
        _props.Update(dt, _activity, state);
        Ease(ref _act, _activity != null ? 1f : 0f, dt, 5f);
        _emotionHold -= dt;
        if (_emotionHold <= 0f)
            _emotion = Emotion.Neutral;
        if (_gesture != null)
        {
            _gestureTime += dt;
            if (_gestureTime >= _gesture.Duration)
                _gesture = null;
        }
        UpdateFidgets(dt, state);

        Ease(ref _climb, state is MascotState.Climb or MascotState.Cling ? 1f : 0f, dt);
        Ease(ref _lie, state == MascotState.Lie ? 1f : 0f, dt, 3f);
        Ease(ref _sprawl, state == MascotState.Sprawl ? 1f : 0f, dt, state == MascotState.Sprawl ? 16f : 4f);
        Ease(ref _tumble, state == MascotState.Tumble ? 1f : 0f, dt, state == MascotState.Tumble ? 14f : 4f);
        Ease(ref _sit, state is MascotState.Sit or MascotState.FloorSit ? 1f : 0f, dt, 6f);
        Ease(ref _fall, state == MascotState.Falling && !bracing ? 1f : 0f, dt);
        Ease(ref _jump, state == MascotState.Jump || (state == MascotState.Falling && bracing) ? 1f : 0f, dt, 12f);
        Ease(ref _drag, state == MascotState.Dragged ? 1f : 0f, dt);
        Ease(ref _land, state == MascotState.Landing ? 1f : 0f, dt, 16f);
        Ease(ref _getUp, state == MascotState.GetUp ? 1f : 0f, dt, 6f);
        Ease(ref _slip, state == MascotState.Climb && grip == ClimbGrip.Slipping ? 1f : 0f, dt, 16f);
        Ease(ref _hang, (state is MascotState.Climb or MascotState.Cling) && grip == ClimbGrip.Hanging ? 1f : 0f, dt, 7f);
        float push = -dragVelocityMetres * 1.5f;
        _dangleVelocity += (push - _dangle * 40f - _dangleVelocity * 5f) * dt;
        _dangle = Mathf.Clamp(_dangle + _dangleVelocity * dt, -0.9f, 0.9f);
        _lookYaw = Mathf.Lerp(_lookYaw, Mathf.Clamp(LookYaw, -0.6f, 0.6f), 1f - MathF.Exp(-6f * dt));
        _lookPitch = Mathf.Lerp(_lookPitch, Mathf.Clamp(LookPitch, -0.4f, 0.4f), 1f - MathF.Exp(-6f * dt));
        Ease(ref _walk, state == MascotState.Walk ? 1f : 0f, dt, 7f);
        Ease(ref _run, running && state == MascotState.Walk ? 1f : 0f, dt, 5f);

        var (clip, speed) = _activity != null ? ActivityClip(_activity) : ChooseClip(state, speedMetres, climbSpeedMetres, running, grip);
        _rig?.Reset();
        PlayClip(clip, speed);
        _player?.Advance(dt);
        // Where the walk clip is in its stride, for what rocks along with the steps.
        if (_player != null && _player.IsPlaying() && clip == "walk")
            _stepPhase = Mathf.Tau * (float)(_player.CurrentAnimationPosition / Math.Max(_player.CurrentAnimationLength, 0.01));
        PoseWhole();
        if (_rig != null)
            PoseBones(dt);
        else
            PoseLegs();
    }

    /// <summary>Munching in the eat clip; otherwise idling, with the legs and body posed over it.</summary>
    private (string? Clip, float Speed) ActivityClip(string activity) => activity switch
    {
        "eat" => _props.FoodLeft > 0f ? ("eat", 1f) : ("yes", 1f),
        "bone" => ("eat", 0.7f),
        "yarn" or "ball" => ("idle", 1.6f),
        "roll" => ("idle", 2f),
        _ => ("idle", 1f),
    };

    /// <summary>
    /// Front legs batting at the ball or wiping the face, legs kicking in the mud, paws out
    /// for the bone: turned on top of whatever the clip did to them this frame.
    /// </summary>
    private void PoseLegs()
    {
        float w = _act;
        float t = _props.Time;
        float[] front = { 0f, 0f }, back = { 0f, 0f };
        if (w > 0.001f)
        {
            switch (_activity)
            {
                case "yarn":
                case "ball":
                {
                    float swat = BallNear() * MathF.Max(0f, MathF.Sin(t * 9f));
                    front[MathF.Sin(t * 1.1f) > 0f ? 0 : 1] = -1.3f * swat;
                    break;
                }
                case "wash_face":
                    front[0] = -1.9f - 0.25f * MathF.Sin(t * 7f);
                    break;
                case "bone":
                    front[0] = front[1] = -1.2f;
                    break;
                case "roll":
                    for (int i = 0; i < 2; i++)
                    {
                        front[i] = 0.6f * MathF.Sin(t * 9f + i * 1.7f);
                        back[i] = 0.6f * MathF.Sin(t * 9f + i * 1.7f + 2f);
                    }
                    break;
            }
        }
        for (int i = 0; i < 2; i++)
        {
            Turn(_frontLegs[i], front[i] * w);
            Turn(_backLegs[i], back[i] * w);
        }
    }

    private void Turn(Node3D? leg, float swing)
    {
        if (leg == null)
            return;
        // A leg the clip did not touch still has last frame's turn: start from its base again.
        var now = leg.Transform;
        var start = _legs.TryGetValue(leg, out var last) && now.IsEqualApprox(last.Written) ? last.Base : now;
        var turned = MathF.Abs(swing) < 0.0001f ? start : new Transform3D(start.Basis * new Basis(Vector3.Right, swing), start.Origin);
        leg.Transform = turned;
        _legs[leg] = (start, turned);
    }

    /// <summary>How close the ball has come to the front paws, 0 to 1.</summary>
    private float BallNear() =>
        Mathf.Clamp(1f - (_props.BallPosition.Z - _props.Front * 0.75f) / (_size.Y * 0.4f), 0f, 1f);

    private (string? Clip, float Speed) ChooseClip(MascotState state, float speedMetres, float climbSpeedMetres, bool running, ClimbGrip grip)
    {
        if (_gesture != null && GestureClip(_gesture.Gesture) is string g && _clips.ContainsKey(g))
            return (g, 1f);
        // Metres the body goes per second at the clip's own speed, so the paws do not slide:
        // the low-poly models' walk (measured from its footfalls) is much shorter-striding.
        float stride = _rig != null ? 0.12f : 0.3f;
        return state switch
        {
            MascotState.Walk when running && _clips.ContainsKey("run") => ("run", Mathf.Clamp(MathF.Abs(speedMetres) / (stride * 2.2f), 0.7f, 2f)),
            MascotState.Walk => ("walk", Mathf.Clamp(MathF.Abs(speedMetres) / stride, 0.6f, _rig != null ? 3.4f : 2.2f)),
            // Climbing paws along to its heaves; scrabbling when slipping; still when hanging.
            MascotState.Climb when grip == ClimbGrip.Slipping => (_clips.ContainsKey("run") ? "run" : "walk", 2.6f),
            MascotState.Climb when grip == ClimbGrip.Hanging => ("idle", 0.6f),
            MascotState.Climb => ("walk", Mathf.Clamp(MathF.Abs(climbSpeedMetres) / (stride * 0.6f), 0.15f, 2.2f)),
            MascotState.Dance => (_clips.ContainsKey("dance") ? "dance" : "idle", 1f),
            MascotState.Falling or MascotState.Dragged => (_clips.ContainsKey("run") ? "run" : "walk", state == MascotState.Dragged ? 1.4f : 2f),
            MascotState.Lie or MascotState.Sprawl => ("idle", 0.35f),
            _ => (_emotion == Emotion.Happy && _clips.ContainsKey("dance") && _emotionHold > 0f ? "dance" : "idle", 1f),
        };
    }

    /// <summary>The clip a gesture shows as: a happy wriggle, a head shake, nibbling, a jump.</summary>
    private static string? GestureClip(Gesture gesture) => gesture switch
    {
        Gesture.Nod or Gesture.Cheer or Gesture.Clap or Gesture.ThumbsUp or Gesture.Wave or Gesture.Giggle
            or Gesture.FistPump or Gesture.Idea or Gesture.Bow => "yes",
        Gesture.Shake or Gesture.Pout or Gesture.Facepalm or Gesture.Shrug or Gesture.Droop or Gesture.Sigh => "no",
        Gesture.Hop or Gesture.Startle => "jump",
        Gesture.LookAround or Gesture.FixHair or Gesture.Yawn or Gesture.KickLegs => "eat",
        _ => null,
    };

    private void PlayClip(string? role, float speed)
    {
        if (_player == null)
            return;
        if (role == null || !_clips.TryGetValue(role, out var clip))
        {
            if (!_clips.TryGetValue("idle", out clip))
                return;
        }
        if (clip != _playing)
        {
            _player.Play(clip, customBlend: 0.2f);
            _playing = clip;
        }
        _player.SpeedScale = speed;
    }

    private void UpdateFidgets(float dt, MascotState state)
    {
        if (!Fidgets || state != MascotState.Idle || _activity != null || _gesture != null || Talking || Petted)
        {
            _fidgetTimer = MathF.Max(_fidgetTimer, 2.5f);
            return;
        }
        _fidgetTimer -= dt;
        if (_fidgetTimer > 0f)
            return;
        _fidgetTimer = 5f + 7f * (float)_random.NextDouble();
        var pick = _random.Next(3) switch { 0 => Gesture.LookAround, 1 => Gesture.Nod, _ => Gesture.Hop };
        Play(new GestureCue(pick, 1.6f, 0.6f, false));
    }

    /// <summary>Whole-model poses for what the clips cannot show.</summary>
    private void PoseWhole()
    {
        float h = _size.Y, depth = _size.Z, width = _size.X;
        float pitch = 0f, roll = 0f, yaw = 0f;
        var offset = Vector3.Zero;
        float squash = 0f;

        // Climbing: nose up, belly to the wall; heaving makes it rock side to side, a slip
        // shakes it, hanging lets it swing.
        pitch += -MathF.PI / 2f * _climb;
        offset.Z += depth * 0.5f * _climb;
        offset.Y += depth * 0.15f * _climb;
        roll += 0.1f * MathF.Sin(_time * 6f) * _climb * (1f - _slip - _hang);
        roll += 0.14f * MathF.Sin(_time * 30f) * _slip;
        pitch += 0.15f * _slip;
        roll += 0.15f * MathF.Sin(_time * 3.4f) * _hang;

        // Lying down: settled on its belly, squashed low with a slight lean to one side
        // (rolling the whole model onto its side sank it through the floor on the way);
        // or flat on its face after a long fall.
        if (_rig == null)
        {
            roll += _lieSide * 0.18f * _lie;
            squash += -0.28f * _lie;
        }
        pitch += MathF.PI / 2f * 0.85f * _sprawl;
        offset.Y += depth * 0.35f * _sprawl;
        // Sitting back on its rump; dazed on its bottom after a hard landing.
        pitch += (_rig == null ? -0.35f * _sit : 0f) - 0.6f * _tumble;
        roll += 0.25f * MathF.Sin(_time * 9f) * MathF.Exp(-_stateTime * 1.2f) * _tumble;
        // Getting back up: a wobble.
        if (_rig == null)
            roll += 0.15f * MathF.Sin(_time * 10f) * _getUp;

        // Carried: dangling and swinging with the motion; falling: spinning a little,
        // legs pedalling (the run clip); hopping down on purpose: leaning into it.
        roll += _dangle * _drag;
        pitch += (0.25f + 0.1f * MathF.Sin(_time * 4f)) * _drag;
        pitch += 0.3f * _fall;
        roll += 0.25f * MathF.Sin(_time * 7f) * _fall;
        pitch += 0.25f * _jump;

        // Landing and talking squash and stretch; petting wiggles; moods.
        squash += (_rig == null ? -0.18f : -0.05f) * _land;
        if (Talking)
            squash += 0.04f * MathF.Sin(_time * 14f);
        if (Petted)
        {
            roll += 0.08f * MathF.Sin(_time * 8f);
            squash += -0.05f;
        }
        squash += _emotion switch
        {
            Emotion.Sad or Emotion.Worried => -0.06f,
            Emotion.Surprised => 0.08f * MathF.Exp(-MathF.Max(0f, 4f - _emotionHold) * 3f),
            _ => 0f,
        };
        if (_rig == null)
            yaw += 0.5f * _lookYaw * (1f - MathF.Max(_climb, MathF.Max(_lie, _sprawl)));
        PoseActivity(ref pitch, ref roll, ref yaw, ref offset, ref squash);

        _root.Transform = new Transform3D(Basis.FromEuler(new Vector3(pitch, yaw, roll), EulerOrder.Yxz), offset);
        Body.Scale = new Vector3(1f - 0.4f * squash, 1f + squash, 1f - 0.4f * squash);
    }

    /// <summary>The whole-model part of an activity: leaning into the bowl, a play bow, rolling over.</summary>
    private void PoseActivity(ref float pitch, ref float roll, ref float yaw, ref Vector3 offset, ref float squash)
    {
        float w = _act;
        if (w <= 0.001f || _activity == null)
            return;
        float t = _props.Time, h = _size.Y;
        // Only the roll is left to the whole model where the bones do the rest.
        if (_rig != null && _activity != "roll")
            return;
        switch (_activity)
        {
            case "eat":
                pitch += (_props.FoodLeft > 0f ? 0.18f : 0f) * w;
                break;
            case "yarn":
            case "ball":
            {
                // Eyes on the ball, chest down and rump up, lunging when it comes close.
                var ball = _props.BallPosition;
                float near = BallNear();
                yaw += 0.6f * Mathf.Atan2(ball.X, MathF.Max(ball.Z, 0.01f)) * w;
                pitch += (0.16f + 0.08f * near) * w;
                offset.Z += h * 0.12f * near * MathF.Max(0f, MathF.Sin(t * 9f)) * w;
                roll += 0.06f * MathF.Sin(t * 8f) * (1f - near) * w;
                squash += -0.05f * w;
                break;
            }
            case "wash_face":
                pitch += -0.35f * w;
                roll += 0.08f * MathF.Sin(t * 7f) * w;
                break;
            case "bone":
                squash += -0.25f * w;
                roll += 0.12f * w;
                break;
            case "roll":
            {
                float side = MathF.Sin(t * 1.6f);
                roll += 1.1f * side * w;
                offset.Y += h * 0.25f * MathF.Abs(side) * w;
                offset.X += -h * 0.2f * side * w;
                squash += -0.15f * w;
                break;
            }
        }
    }

    private static void Ease(ref float value, float target, float dt, float rate = WeightRate) =>
        value = Mathf.Lerp(value, target, 1f - MathF.Exp(-rate * dt));
}
