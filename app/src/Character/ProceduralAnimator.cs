using System;
using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Mascot;
using Godot;

namespace DigitalDoll.Character;

/// <summary>
/// Hand-written procedural motion for every behaviour (idle, walk, dragged, falling,
/// climbing, lying…). Each behaviour is a layer whose weight eases in and out, so
/// state changes blend instead of popping; gestures, moods, petting and talking are
/// layered on top. Angles are in radians, in the character frame given by
/// <see cref="HumanoidRig"/>.
/// </summary>
public sealed partial class ProceduralAnimator
{
    private const float ArmsDown = 1.22f;
    private const float WeightRate = 9f;

    private readonly HumanoidRig _rig;
    private readonly ExpressionRig _expressions;
    private readonly Random _random;
    private readonly float _legScale;

    private float _time;
    private float _walkPhase;
    private float _idle = 1f, _walk, _drag, _fall, _land, _climb, _sit, _jump, _tumble;
    private float _lie, _sprawl, _getUp, _run;
    private float _tumbleTime, _sprawlTime, _lieTime;
    private int _lieSide = 1;
    private float _pet, _petBody;

    private Emotion _emotion = Emotion.Neutral;
    private float _emotionLevel, _emotionTarget, _emotionHold;

    private float _talk, _syllable, _syllableLength = 0.12f;
    private string _viseme = "aa";
    private static readonly string[] Visemes = { "aa", "ih", "ou", "ee", "oh" };

    private readonly PerformanceDirector _fidgetDirector;
    private float _fidgetTimer = 6f;
    private MascotState _state;
    private float _climbPhase;
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
        _fidgetDirector = new PerformanceDirector(random);
    }

    /// <summary>Plays small idle motions (stretching, looking around…) now and then.</summary>
    public bool Fidgets { get; set; } = true;

    /// <summary>Set while the character is speaking (the text is being revealed).</summary>
    public bool Talking { get; set; }

    /// <summary>Being petted (on the head) or rubbed (on the body).</summary>
    public bool Petted { get; set; }
    public TouchZone PetZone { get; set; }

    /// <summary>Shows a mood that fades after <paramref name="seconds"/>.</summary>
    public void Feel(Emotion emotion, float intensity, float seconds = 4f)
    {
        if (emotion == Emotion.Neutral)
        {
            _emotionTarget = 0f;
            return;
        }
        if (emotion != _emotion)
            _emotionLevel *= 0.3f;
        _emotion = emotion;
        _emotionTarget = Mathf.Clamp(intensity, 0f, 1f);
        _emotionHold = seconds;
    }

    /// <summary>Desired head direction in the character frame (radians), set by the caller.</summary>
    public float LookYaw { get; set; }
    public float LookPitch { get; set; }

    public void Update(float dt, MascotState state, float speedMetres, float dragVelocityMetres, float climbSpeedMetres = 0f, bool running = false)
    {
        _time += dt;
        if (state == MascotState.Lie && _state != MascotState.Lie)
            _lieSide = _random.Next(2) == 0 ? -1 : 1;
        _state = state;
        Ease(ref _lie, state == MascotState.Lie ? 1f : 0f, dt, state == MascotState.Lie ? 2.2f : 3.5f);
        Ease(ref _sprawl, state == MascotState.Sprawl ? 1f : 0f, dt, state == MascotState.Sprawl ? 16f : 3.5f);
        Ease(ref _getUp, state == MascotState.GetUp ? 1f : 0f, dt, 5f);
        Ease(ref _run, running ? 1f : 0f, dt, 6f);
        Ease(ref _pet, Petted && PetZone == TouchZone.Head ? 1f : 0f, dt, 5f);
        Ease(ref _petBody, Petted && PetZone == TouchZone.Body ? 1f : 0f, dt, 5f);
        Ease(ref _talk, Talking ? 1f : 0f, dt, 10f);
        _sprawlTime = state == MascotState.Sprawl ? _sprawlTime + dt : 0f;
        _lieTime = state == MascotState.Lie ? _lieTime + dt : 0f;
        AdvanceGestures(dt);
        UpdateFidgets(dt, state);
        Ease(ref _climb, state is MascotState.Climb or MascotState.Cling ? 1f : 0f, dt);
        Ease(ref _sit, state == MascotState.Sit ? 1f : 0f, dt, 6f);
        Ease(ref _jump, state == MascotState.Jump ? 1f : 0f, dt, 14f);
        // Thrown down hard, then a slower climb back up.
        Ease(ref _tumble, state == MascotState.Tumble ? 1f : 0f, dt, state == MascotState.Tumble ? 18f : 4f);
        _tumbleTime = state == MascotState.Tumble ? _tumbleTime + dt : 0f;
        // Hand over hand: one full cycle per ~0.55 character-heights climbed.
        _climbPhase = (_climbPhase + dt * Mathf.Tau * MathF.Abs(climbSpeedMetres) / (0.55f * _rig.Height)) % Mathf.Tau;
        Ease(ref _idle, state == MascotState.Idle ? 1f : 0f, dt);
        Ease(ref _walk, state == MascotState.Walk ? 1f : 0f, dt);
        Ease(ref _drag, state == MascotState.Dragged ? 1f : 0f, dt);
        Ease(ref _fall, state == MascotState.Falling ? 1f : 0f, dt);
        Ease(ref _land, state == MascotState.Landing ? 1f : 0f, dt, 16f);

        float strideLength = (0.95f + 0.5f * _run) * _legScale;
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
        PoseClimb();
        PoseSit();
        PoseJump();
        PoseTumble();
        PoseLie();
        PoseSprawl();
        PoseGetUp();
        PosePetting();
        PoseTalking();
        PoseGestures();
        PoseHead();
        _rig.Commit();

        UpdateFace(dt);
    }

    private void PoseBase()
    {
        // Lower the arms from the rest pose (T or A); dragging and falling raise them again.
        float lower = ArmsDown * (1f - 0.35f * _drag - 1.6f * _fall - 0.5f * _jump) - _rig.ArmDroop;
        var f = _rig.Forward;
        _rig.Rotate("LeftUpperArm", f, -lower);
        _rig.Rotate("RightUpperArm", f, lower);
        _rig.Rotate("LeftLowerArm", _rig.LeftElbowHinge, 0.18f);
        _rig.Rotate("RightLowerArm", _rig.RightElbowHinge, 0.18f);
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
        _rig.Rotate("Spine", left, (0.06f + 0.14f * _run) * w);

        // Arms swing opposite to the legs, bent and pumping when running.
        float swing = 0.38f + 0.3f * _run;
        _rig.Rotate("LeftUpperArm", left, swing * s * w);
        _rig.Rotate("RightUpperArm", left, -swing * s * w);
        _rig.Rotate("LeftLowerArm", _rig.LeftElbowHinge, 1.1f * _run * w);
        _rig.Rotate("RightLowerArm", _rig.RightElbowHinge, 1.1f * _run * w);
        _rig.Rotate("LeftLowerArm", _rig.LeftElbowHinge, 0.25f * MathF.Max(0f, -s) * w);
        _rig.Rotate("RightLowerArm", _rig.RightElbowHinge, 0.25f * MathF.Max(0f, s) * w);
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

    private void PoseClimb()
    {
        float w = _climb;
        if (w <= 0.001f)
            return;
        var left = _rig.Left;
        float s = MathF.Sin(_climbPhase);
        // Arms reach up the wall in turns; the opposite knee lifts with each reach.
        _rig.Rotate("LeftUpperArm", left, -(2.45f + 0.35f * s) * w);
        _rig.Rotate("RightUpperArm", left, -(2.45f - 0.35f * s) * w);
        _rig.Rotate("LeftLowerArm", _rig.LeftElbowHinge, (0.55f - 0.3f * s) * w);
        _rig.Rotate("RightLowerArm", _rig.RightElbowHinge, (0.55f + 0.3f * s) * w);
        _rig.Rotate("LeftUpperLeg", left, -(0.55f - 0.4f * s) * w);
        _rig.Rotate("RightUpperLeg", left, -(0.55f + 0.4f * s) * w);
        _rig.Rotate("LeftLowerLeg", left, (1.0f - 0.35f * s) * w);
        _rig.Rotate("RightLowerLeg", left, (1.0f + 0.35f * s) * w);
        _rig.Rotate("Spine", left, 0.12f * w);
        _rig.Rotate("Head", left, -0.3f * w);
        // Hold-still breathing while clinging.
        _rig.OffsetHips(_rig.Up * (0.01f * _legScale * MathF.Sin(_time * 1.6f) * w));
    }

    private void PoseSit()
    {
        float w = _sit;
        if (w <= 0.001f)
            return;
        var left = _rig.Left;
        // Sit on the edge of the surface: hips drop to the feet line, legs dangle over it.
        _rig.OffsetHips(_rig.Up * (-_rig.HipHeight * 0.93f * w));
        float swing = MathF.Sin(_time * 2.3f);
        _rig.Rotate("LeftUpperLeg", left, -1.5f * w);
        _rig.Rotate("RightUpperLeg", left, -1.5f * w);
        _rig.Rotate("LeftLowerLeg", left, (1.45f + 0.18f * swing) * w);
        _rig.Rotate("RightLowerLeg", left, (1.45f - 0.18f * swing) * w);
        _rig.Rotate("LeftUpperLeg", _rig.Forward, -0.08f * w);
        _rig.Rotate("RightUpperLeg", _rig.Forward, 0.08f * w);
        _rig.Rotate("Spine", left, 0.06f * w);
        // Hands resting on the thighs.
        _rig.Rotate("LeftUpperArm", left, -0.2f * w);
        _rig.Rotate("RightUpperArm", left, -0.2f * w);
        _rig.Rotate("LeftLowerArm", _rig.LeftElbowHinge, 0.95f * w);
        _rig.Rotate("RightLowerArm", _rig.RightElbowHinge, 0.95f * w);
    }

    private void PoseJump()
    {
        float w = _jump;
        if (w <= 0.001f)
            return;
        var left = _rig.Left;
        _rig.Rotate("LeftUpperLeg", left, -0.7f * w);
        _rig.Rotate("RightUpperLeg", left, -0.35f * w);
        _rig.Rotate("LeftLowerLeg", left, 1.1f * w);
        _rig.Rotate("RightLowerLeg", left, 0.7f * w);
        _rig.Rotate("Spine", left, 0.1f * w);
        _rig.Rotate("LeftUpperArm", left, -0.5f * w);
        _rig.Rotate("RightUpperArm", left, -0.2f * w);
    }

    private void PoseTumble()
    {
        float w = _tumble;
        if (w <= 0.001f)
            return;
        var left = _rig.Left;
        // On its bottom with the legs out in front, leaning back on the hands; a
        // little bounce on impact, then shaking the head as if dazed.
        float bounce = MathF.Exp(-_tumbleTime * 9f) * MathF.Sin(_tumbleTime * 30f);
        _rig.OffsetHips(_rig.Up * (-_rig.HipHeight * (0.88f - 0.06f * bounce) * w));
        _rig.Rotate("LeftUpperLeg", left, -1.25f * w);
        _rig.Rotate("RightUpperLeg", left, -1.1f * w);
        _rig.Rotate("LeftLowerLeg", left, 0.25f * w);
        _rig.Rotate("RightLowerLeg", left, 0.55f * w);
        _rig.Rotate("LeftUpperLeg", _rig.Forward, -0.2f * w);
        _rig.Rotate("RightUpperLeg", _rig.Forward, 0.2f * w);
        _rig.Rotate("Spine", left, (-0.3f + 0.15f * bounce) * w);
        _rig.Rotate("LeftUpperArm", left, 0.55f * w);
        _rig.Rotate("RightUpperArm", left, 0.55f * w);
        _rig.Rotate("LeftUpperArm", _rig.Forward, -0.25f * w);
        _rig.Rotate("RightUpperArm", _rig.Forward, 0.25f * w);
        float daze = MathF.Sin(_tumbleTime * 9f) * MathF.Exp(-_tumbleTime * 1.2f);
        _rig.Rotate("Head", _rig.Up, 0.35f * daze * w);
        _rig.Rotate("Head", _rig.Forward, 0.12f * daze * w);
    }

    private void PoseHead()
    {
        float w = (1f - 0.7f * MathF.Max(_drag, _fall)) * (1f - MathF.Max(MathF.Max(_lie, _sprawl), _getUp));
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

        // Moods ease in, hold, then fade back to the behaviour's own face.
        _emotionHold -= dt;
        if (_emotionHold <= 0f && !Talking)
            _emotionTarget = 0f;
        _emotionLevel = Mathf.Lerp(_emotionLevel, _emotionTarget, 1f - MathF.Exp(-(_emotionTarget > _emotionLevel ? 8f : 2f) * dt));

        _expressions.Begin();
        float mood = MathF.Max(_emotionLevel, MathF.Max(_pet, _petBody));
        float closed = MathF.Max(MathF.Max(_lie, _pet * 0.85f), 0.5f * _sprawl);
        _expressions.Set("blink", MathF.Max(MathF.Sin(_blink / 0.16f * MathF.PI), closed));
        _expressions.Set("happy", (0.25f * _idle + 0.15f * _walk + 0.35f * _sit + 0.3f * _jump) * (1f - mood));
        SetMood(_emotion, _emotionLevel);
        if (_pet > 0.01f)
        {
            _expressions.Set("happy", 0.6f * _pet);
            _expressions.Set("relaxed", 0.5f * _pet);
        }
        if (_petBody > 0.01f)
            SetMood(Emotion.Shy, _petBody);
        if (_emotionLevel < 0.2f && (_gesture?.Cue.Gesture is Gesture g) && GestureFace(g) is (string face, float faceWeight))
            _expressions.Set(face, faceWeight * (1f - _emotionLevel));
        if (_lie > 0.01f)
            _expressions.Set("relaxed", 0.6f * _lie);
        if (_sprawl > 0.01f)
            _expressions.Set(_expressions.Has("sad") ? "sad" : "angry", 0.7f * _sprawl);
        if (_gesture?.Cue.Gesture == Gesture.Yawn)
            _expressions.Set("oh", 0.9f * MathF.Sin(MathF.PI * _gesture.Phase));
        UpdateMouth(dt);
        float startled = 0.7f * MathF.Max(_drag, _fall);
        // Eyes squeezed shut and a pained face for a moment after a hard landing.
        if (_tumble > 0.01f)
        {
            float wince = _tumble * MathF.Max(0f, 1f - _tumbleTime / 1.2f);
            _expressions.Set("blink", wince * 0.8f);
            _expressions.Set(_expressions.Has("sad") ? "sad" : "angry", 0.6f * _tumble);
        }
        // VRM 0.x models have no "surprised" preset; an open mouth reads similarly.
        _expressions.Set(_expressions.Has("surprised") ? "surprised" : "oh", startled);
        _expressions.Commit();
    }

    private void SetMood(Emotion emotion, float level)
    {
        if (level <= 0.01f)
            return;
        switch (emotion)
        {
            case Emotion.Happy:
                _expressions.Set("happy", level);
                break;
            case Emotion.Angry:
                _expressions.Set("angry", level);
                break;
            case Emotion.Sad:
                _expressions.Set("sad", level);
                break;
            case Emotion.Relaxed:
                _expressions.Set(_expressions.Has("relaxed") ? "relaxed" : "happy", level * 0.8f);
                break;
            case Emotion.Surprised:
                if (_expressions.Has("surprised"))
                    _expressions.Set("surprised", level);
                else
                    _expressions.Set("oh", 0.6f * level);
                break;
            case Emotion.Shy:
                _expressions.Set("happy", 0.45f * level);
                _expressions.Set("relaxed", 0.35f * level);
                _expressions.Set("lookDown", 0.4f * level);
                break;
            case Emotion.Worried:
                _expressions.Set("sad", 0.5f * level);
                break;
        }
    }

    /// <summary>Mouth shapes while talking: a new vowel every syllable.</summary>
    private void UpdateMouth(float dt)
    {
        if (_talk <= 0.01f)
            return;
        _syllable += dt;
        if (_syllable >= _syllableLength)
        {
            _syllable = 0f;
            _syllableLength = 0.09f + 0.08f * (float)_random.NextDouble();
            _viseme = Visemes[_random.Next(Visemes.Length)];
        }
        float open = MathF.Sin(MathF.PI * _syllable / _syllableLength);
        _expressions.Set(_viseme, 0.65f * open * _talk);
    }

    private void UpdateFidgets(float dt, MascotState state)
    {
        bool calm = state is MascotState.Idle or MascotState.Sit or MascotState.Cling;
        if (!Fidgets || !calm || _gesture != null || Talking || Petted || _emotionLevel > 0.3f)
        {
            _fidgetTimer = MathF.Max(_fidgetTimer, 2.5f);
            return;
        }
        _fidgetTimer -= dt;
        if (_fidgetTimer > 0f)
            return;
        _fidgetTimer = 5f + 7f * (float)_random.NextDouble();
        Gesture[] options = state switch
        {
            MascotState.Sit => new[] { Gesture.KickLegs, Gesture.KickLegs, Gesture.LookAround, Gesture.FixHair, Gesture.Yawn, Gesture.Stretch },
            MascotState.Cling => new[] { Gesture.LookAround },
            _ => new[] { Gesture.Stretch, Gesture.LookAround, Gesture.FixHair, Gesture.Yawn, Gesture.ShiftWeight, Gesture.ShiftWeight, Gesture.Tilt },
        };
        var pick = options[_random.Next(options.Length)];
        if (pick == _lastFidget && options.Length > 1)
            pick = options[(Array.IndexOf(options, pick) + 1) % options.Length];
        _lastFidget = pick;
        Play(_fidgetDirector.Vary(pick, 0.4f + 0.4f * (float)_random.NextDouble()));
    }

    private Gesture? _lastFidget;

    private void PoseLie()
    {
        float w = _lie;
        if (w <= 0.001f)
            return;
        var left = _rig.Left;
        var f = _rig.Forward;
        int side = _lieSide;
        // On its side facing the viewer, head on the lower arm, knees drawn up a little.
        _rig.OffsetHips(_rig.Up * (-(_rig.HipHeight - 0.21f * _rig.Height) * w));
        _rig.Rotate("Hips", f, side * MathF.PI / 2f * w);
        float breath = MathF.Sin(_lieTime * Mathf.Tau / 5f);
        _rig.Rotate("Chest", left, -0.03f * breath * w);
        _rig.Rotate("LeftUpperLeg", left, -0.55f * w);
        _rig.Rotate("RightUpperLeg", left, -0.35f * w);
        _rig.Rotate("LeftLowerLeg", left, 0.9f * w);
        _rig.Rotate("RightLowerLeg", left, 0.7f * w);
        // The lower arm reaches past the head as a pillow; the upper one rests in front.
        int lower = -side;
        Arm(lower, 2.6f, 0f, 0.6f, 0f, 0f, w);
        Arm(side, 0f, 0.45f, 0.9f, 0.35f, 0f, w);
        _rig.Rotate("Neck", f, -side * 0.15f * w);
        _rig.Rotate("Head", f, -side * 0.2f * w);
        _rig.Rotate("Head", left, 0.12f * w);
    }

    private void PoseSprawl()
    {
        float w = _sprawl;
        if (w <= 0.001f)
            return;
        var left = _rig.Left;
        // Flat on its face, arms flung forward; a bounce on impact, then a feeble kick.
        float bounce = MathF.Exp(-_sprawlTime * 8f) * MathF.Sin(_sprawlTime * 26f);
        _rig.OffsetHips(_rig.Up * (-(_rig.HipHeight - (0.16f + 0.03f * bounce) * _rig.Height) * w));
        _rig.Rotate("Hips", left, MathF.PI / 2f * w);
        _rig.Rotate("Spine", left, -0.08f * w);
        Arm(1, 0.35f, 2.75f, 0.2f, -0.2f, 0f, w);
        Arm(-1, 0.35f, 2.75f, 0.2f, -0.2f, 0f, w);
        float kick = MathF.Max(0f, MathF.Sin(_sprawlTime * 7f)) * Smooth(Mathf.Clamp((_sprawlTime - 0.8f) / 0.4f, 0f, 1f))
                     * MathF.Exp(-MathF.Max(0f, _sprawlTime - 1.2f) * 1.5f);
        _rig.Rotate("LeftLowerLeg", left, (0.15f + 0.9f * kick) * w);
        _rig.Rotate("RightLowerLeg", left, (0.15f + 0.9f * (1f - kick) * kick * 2f) * w);
        _rig.Rotate("Head", left, -0.35f * w);
    }

    private void PoseGetUp()
    {
        float w = _getUp;
        if (w <= 0.001f)
            return;
        var left = _rig.Left;
        // Down on one knee, pushing up off the thigh.
        _rig.OffsetHips(_rig.Up * (-_rig.HipHeight * 0.42f * w));
        _rig.Rotate("LeftUpperLeg", left, -1.4f * w);
        _rig.Rotate("LeftLowerLeg", left, 1.5f * w);
        _rig.Rotate("LeftFoot", left, -0.2f * w);
        _rig.Rotate("RightUpperLeg", left, -0.15f * w);
        _rig.Rotate("RightLowerLeg", left, 1.65f * w);
        _rig.Rotate("RightFoot", left, 0.5f * w);
        _rig.Rotate("Spine", left, 0.35f * w);
        Arm(1, 0f, 0.6f, 0.6f, 0.2f, 0f, w);
        Arm(-1, 0.2f, 0.3f, 0.3f, 0f, 0f, w);
        _rig.Rotate("Head", left, -0.2f * w);
    }

    private void PosePetting()
    {
        if (_pet > 0.001f)
        {
            // Leaning into the hand: the head sways under it, shoulders up, eyes closed.
            float sway = MathF.Sin(_time * 3.1f);
            Head(0.12f * _pet, 0.06f * sway * _pet, 0.16f * sway * _pet);
            _rig.Rotate("LeftShoulder", _rig.Forward, 0.08f * _pet);
            _rig.Rotate("RightShoulder", _rig.Forward, -0.08f * _pet);
            Arm(1, 0f, 0.3f, 0.5f, 0.3f, 0f, _pet);
            Arm(-1, 0f, 0.3f, 0.5f, 0.3f, 0f, _pet);
            _rig.Rotate("Hips", _rig.Forward, 0.03f * sway * _pet);
        }
        if (_petBody > 0.001f)
        {
            // Squirming: shoulders hunched, arms in, wriggling.
            float wriggle = MathF.Sin(_time * 11f);
            _rig.Rotate("LeftShoulder", _rig.Forward, 0.14f * _petBody);
            _rig.Rotate("RightShoulder", _rig.Forward, -0.14f * _petBody);
            Arm(1, 0f, 0.4f, 1.3f, 0.5f, 0f, _petBody);
            Arm(-1, 0f, 0.4f, 1.3f, 0.5f, 0f, _petBody);
            _rig.Rotate("Spine", _rig.Up, 0.1f * wriggle * _petBody);
            _rig.Rotate("Hips", _rig.Forward, 0.05f * wriggle * _petBody);
            Head(0.15f * _petBody, 0.2f * _petBody, 0.1f * wriggle * _petBody);
        }
    }

    /// <summary>Speaking rhythm: small nods and hand movement even without gestures.</summary>
    private void PoseTalking()
    {
        float w = _talk * Allowance(GestureReach.Upper) * (_gesture == null ? 1f : 0.3f);
        if (w <= 0.001f)
            return;
        float beat = MathF.Sin(_time * 4.3f) * 0.5f + MathF.Sin(_time * 2.1f + 1f) * 0.5f;
        Head(0.05f * MathF.Max(0f, beat) * w, 0.04f * MathF.Sin(_time * 1.3f) * w, 0.04f * MathF.Sin(_time * 0.9f) * w);
        Arm(-1, 0.1f, 0.25f + 0.08f * beat, 0.7f, 0.1f, 0f, w);
        Arm(1, 0.05f, 0.15f, 0.5f, 0.1f, 0f, w * 0.6f);
    }

    private static void Ease(ref float value, float target, float dt, float rate = WeightRate) =>
        value = Mathf.Lerp(value, target, 1f - MathF.Exp(-rate * dt));
}
