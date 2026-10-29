using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Mascot;
using Godot;

namespace DigitalDoll.Character;

/// <summary>Moves a character: behaviours, gestures, moods, talking and petting.</summary>
public interface IPerformer
{
    /// <summary>Desired head direction in the character frame (radians).</summary>
    float LookYaw { get; set; }
    float LookPitch { get; set; }

    bool Fidgets { get; set; }
    bool Talking { get; set; }
    bool Petted { get; set; }
    TouchZone PetZone { get; set; }
    Gesture? CurrentGesture { get; }

    /// <param name="bracing">Falling on purpose (feet first) rather than thrown or knocked off.</param>
    /// <param name="grip">While climbing: slipping down or hanging on after a slip.</param>
    void Update(float dt, MascotState state, float speedMetres, float dragVelocityMetres, float climbSpeedMetres = 0f, bool running = false,
        bool bracing = false, ClimbGrip grip = ClimbGrip.Steady);
    void Play(GestureCue cue, bool now = false);

    /// <summary>
    /// The pose to hold while the behaviour is <see cref="MascotState.Pose"/> (an id from
    /// <see cref="PoseCatalog"/>); characters without the pose do something close instead.
    /// </summary>
    void StrikePose(string id);
    void StopGestures();
    void Feel(Emotion emotion, float intensity, float seconds = 4f);
}

/// <summary>A character shown on the desktop: the doll (a humanoid VRM or edited body) or an animal.</summary>
public interface IMascotBody
{
    Node3D Model { get; }
    IPerformer Performer { get; }

    /// <summary>On-screen height relative to the doll (animals are smaller).</summary>
    float SizeFactor { get; }

    /// <summary>Walking and climbing speeds in model units (metres) per second.</summary>
    float WalkSpeed { get; }
    float ClimbSpeed { get; }

    /// <summary>Who the character is and its name, for the chat (null for the doll).</summary>
    string? Persona { get; }
    string? Name { get; }

    Vector3 HeadPosition { get; }
    Vector3 NeckPosition { get; }

    /// <summary>Turns the head toward a world position (sets the performer's look angles).</summary>
    void LookAt(Vector3 target);

    /// <summary>The mouse region in viewport pixels, grown by <paramref name="margin"/>.</summary>
    Vector2[] Outline(Camera3D camera, float margin);

    /// <summary>Body turn about the vertical axis for a state (0 faces the viewer).</summary>
    float Yaw(MascotState state, int facing, int wallSide, bool faceUser);
}

/// <summary>The doll: a humanoid skeleton driven by <see cref="ProceduralAnimator"/>.</summary>
public sealed class HumanBody : IMascotBody
{
    private const float FacingTurn = 1.1f;

    private readonly HumanoidRig _rig;
    private readonly ProceduralAnimator _animator;
    private readonly CharacterHitShape _hitShape;

    public HumanBody(Node3D model, HumanoidRig rig, ProceduralAnimator animator, CharacterHitShape hitShape)
    {
        Model = model;
        _rig = rig;
        _animator = animator;
        _hitShape = hitShape;
    }

    public Node3D Model { get; }
    public IPerformer Performer => _animator;
    public float SizeFactor => 1f;
    public float WalkSpeed => 0.55f;
    public float ClimbSpeed => 0.32f;
    public string? Persona => null;
    public string? Name => null;
    public HumanoidRig Rig => _rig;

    public Vector3 HeadPosition => _rig.BoneGlobalPosition("Head");
    public Vector3 NeckPosition => _rig.BoneGlobalPosition("Neck");

    public void LookAt(Vector3 target)
    {
        var local = _rig.Skeleton.GlobalBasis.Inverse() * (target - HeadPosition);
        float forward = local.Dot(_rig.Forward);
        float side = local.Dot(_rig.Left);
        float up = local.Dot(_rig.Up);
        _animator.LookYaw = Mathf.Atan2(side, Mathf.Max(forward, 0.05f));
        _animator.LookPitch = Mathf.Atan2(up, new Vector2(forward, side).Length());
    }

    public Vector2[] Outline(Camera3D camera, float margin) => _hitShape.Outline(camera, margin);

    public float Yaw(MascotState state, int facing, int wallSide, bool faceUser) => state switch
    {
        MascotState.Walk => facing * FacingTurn,
        MascotState.Falling or MascotState.Jump => facing * FacingTurn * 0.6f,
        // Side on to the screen edge it is climbing.
        MascotState.Climb or MascotState.Cling => wallSide * Mathf.Pi / 2f,
        MascotState.Dragged or MascotState.Lie or MascotState.Dance => 0f,
        // Sitting on an edge, turned a little so the thighs read instead of pointing at the viewer.
        MascotState.Sit => facing * 0.55f,
        // Three-quarters on, so the legs read instead of pointing at the viewer.
        MascotState.Tumble or MascotState.FloorSit => facing * 0.65f,
        // Flat on its face: seen from the side.
        MascotState.Sprawl => facing * Mathf.Pi / 2f,
        MascotState.GetUp => facing * 0.5f,
        MascotState.Pose => _animator.PoseYaw(facing),
        // Facing the user while talking or being touched.
        MascotState.Idle when faceUser => 0f,
        _ => facing * 0.3f,
    };
}
