using System;
using DigitalDoll.Core.Mascot;
using Godot;

namespace DigitalDoll.Character;

/// <summary>
/// Hand-written procedural motion for the M0 behaviours (idle, walk, dragged,
/// falling, landing). Each behaviour is a layer whose weight eases in and out, so
/// state changes blend instead of popping. Angles are in radians, in the character
/// frame given by <see cref="HumanoidRig"/>.
/// </summary>
public sealed class ProceduralAnimator
{
    private const float ArmsDown = 1.22f;
    private const float WeightRate = 9f;

    private readonly HumanoidRig _rig;
    private readonly ExpressionRig _expressions;
    private readonly Random _random;
    private readonly float _legScale;

    private float _time;
    private float _walkPhase;
    private float _idle = 1f, _walk, _drag, _fall, _land;
    private float _dangle, _dangleVelocity;
    private float _lookYaw, _lookPitch;
    private float _nextBlink = 2f, _blink;
    private bool _doubleBlink;

    public ProceduralAnimator(HumanoidRig rig, ExpressionRig expressions, Random random)
    {
        _rig = rig;
        _expressions = expressions;
        _random = random;
        _legScale = rig.Height / 1.6f;
    }

    /// <summary>Desired head direction in the character frame (radians), set by the caller.</summary>
    public float LookYaw { get; set; }
    public float LookPitch { get; set; }

    public void Update(float dt, MascotState state, float speedMetres, float dragVelocityMetres)
    {
        _time += dt;
        Ease(ref _idle, state == MascotState.Idle ? 1f : 0f, dt);
        Ease(ref _walk, state == MascotState.Walk ? 1f : 0f, dt);
        Ease(ref _drag, state == MascotState.Dragged ? 1f : 0f, dt);
        Ease(ref _fall, state == MascotState.Falling ? 1f : 0f, dt);
        Ease(ref _land, state == MascotState.Landing ? 1f : 0f, dt, 16f);

        float strideLength = 0.95f * _legScale;
        _walkPhase = (_walkPhase + dt * Mathf.Tau * MathF.Abs(speedMetres) / strideLength) % Mathf.Tau;

        // Legs swing like a damped pendulum while the character is carried around.
        float push = -dragVelocityMetres * 0.6f;
        _dangleVelocity += (push - _dangle * 40f - _dangleVelocity * 5f) * dt;
        _dangle = Mathf.Clamp(_dangle + _dangleVelocity * dt, -0.9f, 0.9f);

        _lookYaw = Mathf.Lerp(_lookYaw, Mathf.Clamp(LookYaw, -0.7f, 0.7f), 1f - MathF.Exp(-6f * dt));
        _lookPitch = Mathf.Lerp(_lookPitch, Mathf.Clamp(LookPitch, -0.4f, 0.4f), 1f - MathF.Exp(-6f * dt));

        _rig.Begin();
        PoseBase();
        PoseIdle();
        PoseWalk();
        PoseDragged();
        PoseFalling();
        PoseLanding();
        PoseHead();
        _rig.Commit();

        UpdateFace(dt);
    }

    private void PoseBase()
    {
        // Lower the arms out of the T-pose; dragging and falling raise them again.
        float lower = ArmsDown * (1f - 0.35f * _drag - 1.6f * _fall);
        var f = _rig.Forward;
        _rig.Rotate("LeftUpperArm", f, -lower);
        _rig.Rotate("RightUpperArm", f, lower);
        _rig.Rotate("LeftLowerArm", _rig.Left, -0.18f);
        _rig.Rotate("RightLowerArm", _rig.Left, -0.18f);
        _rig.Rotate("LeftLowerArm", _rig.Up, 0.25f);
        _rig.Rotate("RightLowerArm", _rig.Up, -0.25f);
    }

    private void PoseIdle()
    {
        float breath = MathF.Sin(_time * Mathf.Tau / 4.2f);
        float sway = MathF.Sin(_time * Mathf.Tau / 7.3f);
        float w = 0.35f + 0.65f * _idle;

        _rig.Rotate("Chest", _rig.Left, -0.025f * breath * w);
        _rig.Rotate("LeftShoulder", _rig.Forward, 0.03f * breath * w);
        _rig.Rotate("RightShoulder", _rig.Forward, -0.03f * breath * w);

        w = _idle;
        _rig.Rotate("Hips", _rig.Forward, 0.035f * sway * w);
        _rig.Rotate("Spine", _rig.Forward, -0.05f * sway * w);
        _rig.OffsetHips(_rig.Left * (0.012f * _legScale * sway * w));
        _rig.Rotate("LeftUpperLeg", _rig.Forward, -0.035f * sway * w);
        _rig.Rotate("RightUpperLeg", _rig.Forward, -0.035f * sway * w);
        _rig.Rotate("LeftUpperArm", _rig.Left, 0.04f * MathF.Sin(_time * 0.9f) * w);
        _rig.Rotate("RightUpperArm", _rig.Left, 0.04f * MathF.Sin(_time * 0.9f + 1.3f) * w);
    }

    private void PoseWalk()
    {
        float w = _walk;
        if (w <= 0.001f)
            return;
        float p = _walkPhase;
        float s = MathF.Sin(p);
        var left = _rig.Left;
        var up = _rig.Up;

        // Positive about Left swings a hanging limb backward, so forward is negative.
        _rig.Rotate("LeftUpperLeg", left, -0.42f * s * w);
        _rig.Rotate("RightUpperLeg", left, 0.42f * s * w);

        float kneeL = 0.9f * MathF.Pow(MathF.Max(0f, MathF.Cos(p)), 1.5f) + 0.08f;
        float kneeR = 0.9f * MathF.Pow(MathF.Max(0f, MathF.Cos(p + MathF.PI)), 1.5f) + 0.08f;
        _rig.Rotate("LeftLowerLeg", left, kneeL * w);
        _rig.Rotate("RightLowerLeg", left, kneeR * w);
        _rig.Rotate("LeftFoot", left, (-0.55f * kneeL + 0.15f * s) * w);
        _rig.Rotate("RightFoot", left, (-0.55f * kneeR - 0.15f * s) * w);

        _rig.OffsetHips(up * (0.02f * _legScale * MathF.Cos(2f * p) * w));
        _rig.Rotate("Hips", _rig.Forward, 0.04f * s * w);
        _rig.Rotate("Hips", up, -0.09f * s * w);
        _rig.Rotate("Spine", up, 0.07f * s * w);
        _rig.Rotate("Chest", up, 0.06f * s * w);
        _rig.Rotate("Spine", left, 0.06f * w);

        // Arms swing opposite to the legs.
        _rig.Rotate("LeftUpperArm", left, 0.38f * s * w);
        _rig.Rotate("RightUpperArm", left, -0.38f * s * w);
        _rig.Rotate("LeftLowerArm", left, -0.25f * MathF.Max(0f, -s) * w);
        _rig.Rotate("RightLowerArm", left, -0.25f * MathF.Max(0f, s) * w);
    }

    private void PoseDragged()
    {
        float w = _drag;
        if (w <= 0.001f)
            return;
        var left = _rig.Left;
        var f = _rig.Forward;
        float kick = MathF.Sin(_time * 9f);
        float flap = MathF.Sin(_time * 7f);

        _rig.Rotate("LeftUpperLeg", left, (-0.25f - 0.3f * kick + _dangle) * w);
        _rig.Rotate("RightUpperLeg", left, (-0.25f + 0.3f * kick + _dangle) * w);
        _rig.Rotate("LeftLowerLeg", left, (0.55f + 0.35f * kick) * w);
        _rig.Rotate("RightLowerLeg", left, (0.55f - 0.35f * kick) * w);
        _rig.Rotate("LeftUpperArm", f, 0.3f * flap * w);
        _rig.Rotate("RightUpperArm", f, 0.3f * flap * w);
        _rig.Rotate("Spine", left, (-0.12f + 0.4f * _dangle) * w);
        _rig.Rotate("Head", _rig.Forward, 0.15f * MathF.Sin(_time * 3f) * w);
    }

    private void PoseFalling()
    {
        float w = _fall;
        if (w <= 0.001f)
            return;
        var left = _rig.Left;
        float flail = MathF.Sin(_time * 14f);
        _rig.Rotate("LeftUpperArm", _rig.Forward, 0.25f * flail * w);
        _rig.Rotate("RightUpperArm", _rig.Forward, 0.25f * flail * w);
        _rig.Rotate("LeftUpperLeg", left, -0.35f * w);
        _rig.Rotate("RightUpperLeg", left, -0.1f * w);
        _rig.Rotate("LeftLowerLeg", left, 0.7f * w);
        _rig.Rotate("RightLowerLeg", left, 0.35f * w);
        _rig.Rotate("Spine", left, -0.15f * w);
        _rig.Rotate("Head", left, -0.2f * w);
    }

    private void PoseLanding()
    {
        float w = _land;
        if (w <= 0.001f)
            return;
        var left = _rig.Left;
        // Crouch: thighs forward, knees back, feet kept roughly flat.
        _rig.OffsetHips(_rig.Up * (-0.13f * _legScale * w));
        _rig.Rotate("LeftUpperLeg", left, -0.75f * w);
        _rig.Rotate("RightUpperLeg", left, -0.75f * w);
        _rig.Rotate("LeftLowerLeg", left, 1.3f * w);
        _rig.Rotate("RightLowerLeg", left, 1.3f * w);
        _rig.Rotate("LeftFoot", left, -0.55f * w);
        _rig.Rotate("RightFoot", left, -0.55f * w);
        _rig.Rotate("Spine", left, 0.35f * w);
        _rig.Rotate("LeftUpperArm", left, -0.4f * w);
        _rig.Rotate("RightUpperArm", left, -0.4f * w);
    }

    private void PoseHead()
    {
        float w = 1f - 0.7f * MathF.Max(_drag, _fall);
        // Positive yaw about Up turns the face toward the character's left.
        _rig.Rotate("Neck", _rig.Up, 0.4f * _lookYaw * w);
        _rig.Rotate("Head", _rig.Up, 0.6f * _lookYaw * w);
        // Positive about Left nods down, so looking up is negative.
        _rig.Rotate("Neck", _rig.Left, -0.4f * _lookPitch * w);
        _rig.Rotate("Head", _rig.Left, -0.6f * _lookPitch * w);
    }

    private void UpdateFace(float dt)
    {
        _nextBlink -= dt;
        if (_nextBlink <= 0f)
        {
            _blink = 0.16f;
            if (_doubleBlink)
            {
                _doubleBlink = false;
                _nextBlink = 0.25f;
            }
            else
            {
                _doubleBlink = _random.NextDouble() < 0.2;
                _nextBlink = 2f + (float)_random.NextDouble() * 4f;
            }
        }
        _blink = MathF.Max(0f, _blink - dt);

        _expressions.Begin();
        _expressions.Set("blink", MathF.Sin(_blink / 0.16f * MathF.PI));
        _expressions.Set("happy", 0.25f * _idle + 0.15f * _walk);
        float startled = 0.7f * MathF.Max(_drag, _fall);
        // VRM 0.x models have no "surprised" preset; an open mouth reads similarly.
        _expressions.Set(_expressions.Has("surprised") ? "surprised" : "oh", startled);
        _expressions.Commit();
    }

    private static void Ease(ref float value, float target, float dt, float rate = WeightRate) =>
        value = Mathf.Lerp(value, target, 1f - MathF.Exp(-rate * dt));
}
