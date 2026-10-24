using System;
using DigitalDoll.Core.Body;
using Godot;

namespace DigitalDoll.Body;

/// <summary>
/// Makes the edited body's chest and buttocks bounce: springs follow how the chest
/// and hip joints accelerate (walking, being carried and shaken, falling, landing),
/// and their offsets move those regions in the body and outline shaders. Each side
/// has its own slightly different spring so the two do not move in lockstep.
/// </summary>
public partial class BodyJiggle : Node
{
    // Anime-style: a little livelier than real.
    private const float Gain = 1.4f;
    // Accelerations above this are teleports (a new scene, the character placed
    // somewhere), not motion.
    private const float MaxAcceleration = 3000f;

    private Skeleton3D _skeleton = null!;
    private MeshInstance3D _mesh = null!;
    private ShaderMaterial[] _materials = null!;
    private int _chest, _hips;
    private float _bust, _buttocks;

    private readonly JiggleSpring _bustLeft = new(1.9f, 0.18f, 0.03f);
    private readonly JiggleSpring _bustRight = new(2.0f, 0.19f, 0.03f);
    private readonly JiggleSpring _buttocksLeft = new(2.5f, 0.26f, 0.02f);
    private readonly JiggleSpring _buttocksRight = new(2.6f, 0.27f, 0.02f);
    private Tracker _chestTrack, _hipsTrack;
    private float _stride = 0.95f;
    private float _stepPhase;

    public static void Attach(Skeleton3D skeleton, MeshInstance3D mesh, ShaderMaterial[] materials, BodyShape shape)
    {
        var node = new BodyJiggle
        {
            Name = "Jiggle",
            _skeleton = skeleton,
            _mesh = mesh,
            _materials = materials,
            _chest = skeleton.FindBone("UpperChest") is int c and >= 0 ? c : skeleton.FindBone("Chest"),
            _hips = skeleton.FindBone("Hips"),
            _bust = JiggleRegions.BustAmount(shape),
            _buttocks = JiggleRegions.ButtocksAmount(shape),
        };
        // Same stride as the walk animation (ProceduralAnimator): 0.95 m for a 1.6 m figure.
        int head = skeleton.FindBone("Head");
        if (head >= 0)
            node._stride = 0.95f * skeleton.GetBoneGlobalRest(head).Origin.Y * 1.14f / 1.6f;
        skeleton.AddChild(node);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (dt <= 0f || _chest < 0 || _hips < 0)
            return;
        var toMesh = _mesh.GlobalBasis.Inverse();
        var chest = _chestTrack.Accelerate(BonePosition(_chest), dt);
        var hips = _hipsTrack.Accelerate(BonePosition(_hips), dt);

        // Footfalls: the walk animation's bounce is gentle, so add the jolt of each
        // step (two per stride), stronger the faster it moves.
        float speed = new Vector2(_hipsTrack.Velocity.X, _hipsTrack.Velocity.Z).Length();
        if (speed is > 0.05f and < 6f)
        {
            _stepPhase = (_stepPhase + dt * Mathf.Tau * 2f * speed / _stride) % Mathf.Tau;
            var jolt = Vector3.Up * (9f * Mathf.Clamp(speed / 0.55f, 0f, 2.2f) * MathF.Cos(_stepPhase));
            chest += jolt;
            hips += jolt * 0.8f;
        }

        Step(_bustLeft, toMesh * chest, dt);
        Step(_bustRight, toMesh * chest, dt);
        Step(_buttocksLeft, toMesh * hips, dt);
        Step(_buttocksRight, toMesh * hips, dt);

        foreach (var material in _materials)
        {
            material.SetShaderParameter("bust_offset_l", Offset(_bustLeft, _bust));
            material.SetShaderParameter("bust_offset_r", Offset(_bustRight, _bust));
            material.SetShaderParameter("buttocks_offset_l", Offset(_buttocksLeft, _buttocks));
            material.SetShaderParameter("buttocks_offset_r", Offset(_buttocksRight, _buttocks));
        }
    }

    public override void _ExitTree()
    {
        // The materials are shared with nothing else, but leave them at rest anyway.
        foreach (var material in _materials)
        {
            foreach (var name in new[] { "bust_offset_l", "bust_offset_r", "buttocks_offset_l", "buttocks_offset_r" })
                material.SetShaderParameter(name, Vector3.Zero);
        }
    }

    private Vector3 BonePosition(int bone) => _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(bone).Origin;

    private static void Step(JiggleSpring spring, Vector3 acceleration, float dt) =>
        spring.Update(dt, new System.Numerics.Vector3(acceleration.X, acceleration.Y, acceleration.Z) * Gain);

    private static Vector3 Offset(JiggleSpring spring, float amount) =>
        new Vector3(spring.Offset.X, spring.Offset.Y, spring.Offset.Z) * amount;

    /// <summary>Acceleration of a point from its positions in successive frames.</summary>
    private struct Tracker
    {
        private Vector3 _position, _velocity;
        private int _frames;

        public readonly Vector3 Velocity => _velocity;

        public Vector3 Accelerate(Vector3 position, float dt)
        {
            var velocity = _frames > 0 ? (position - _position) / dt : Vector3.Zero;
            // The first frames after appearing carry the setup, not motion.
            var acceleration = _frames > 3 ? (velocity - _velocity) / dt : Vector3.Zero;
            _position = position;
            _velocity = velocity;
            _frames++;
            if (acceleration.Length() > MaxAcceleration)
            {
                // A jump in position: start tracking afresh from here.
                _velocity = Vector3.Zero;
                return Vector3.Zero;
            }
            return acceleration;
        }
    }
}
