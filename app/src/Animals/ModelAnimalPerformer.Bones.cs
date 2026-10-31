using System;
using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Mascot;
using Godot;
using Ch = DigitalDoll.Animals.ModelAnimalRig.Channel;

namespace DigitalDoll.Animals;

/// <summary>
/// What the model animals with a skeleton do with it: each behaviour adds to the rig's
/// channels with its weight, so walking, sitting, lying, looking, moods, gestures and
/// activities mix and flow into each other. The gait follows how cats really walk: four
/// beats in lateral sequence (right hind, right front, left hind, left front) with each
/// paw down for over half the stride and put down where it stays until it lifts again,
/// the spine bending in a wave, shoulders and hips rolling against each other and the
/// head carried low and steady. The tail is low and relaxed by default and only rises,
/// hooked at the tip, when the animal is happy or being petted.
/// </summary>
public sealed partial class ModelAnimalPerformer
{
    // Offsets into the stride of front-left, front-right, back-left, back-right.
    private static readonly float[] WalkOffsets = { 0.75f, 0.25f, 0.5f, 0f };
    private static readonly float[] GallopOffsets = { 0.5f, 0.62f, 0f, 0.1f };

    /// <summary>Metres per stride and the share of it each paw is on the ground.</summary>
    private const float WalkStride = 0.2f, GallopStride = 0.3f, WalkDuty = 0.62f, GallopDuty = 0.36f;

    // Up a wall: the front paws in turn, the back pair pushing almost together.
    private static readonly float[] ClimbOffsets = { 0f, 0.5f, 0.25f, 0.3f };
    private const float ClimbStride = 0.14f, ClimbDuty = 0.6f;
    private float _climbGait;

    private ModelAnimalRig? _rig;
    private float _walk, _run, _dance, _lookPitch, _nextGlance = 3f, _glance, _glanceHold;
    private float _gait, _gaitSpeed;
    private float _tailMood = 0.9f, _tailMoodTimer = 8f, _nextFlick = 3f, _flick;
    private float _lastYaw, _turnBend;
    private string? _fadingActivity;
    private ModelAnimalEyes? _eyes;
    private float _nextBlink = 2f, _blink;

    /// <summary>Gives a model without eyes a pair (see <see cref="ModelAnimalEyes"/>).</summary>
    public void AddEyes(ModelAnimalEyes.Shape shape)
    {
        if (_rig != null)
            _eyes = new ModelAnimalEyes(_rig.AttachToHead(), shape);
    }

    /// <summary>Where it is held when picked up, for a model with bones.</summary>
    public Vector3? Neck => _rig?.NeckWorld;

    public void SetEyeColour(Color colour) => _eyes?.SetColour(colour);

    /// <summary>Blinks now and then; narrowed when content, shut in sleep, wide when startled or at play.</summary>
    private void PoseEyes(float dt)
    {
        if (_eyes == null)
            return;
        _nextBlink -= dt;
        if (_nextBlink <= 0f)
        {
            _nextBlink = 2.5f + 4.5f * (float)_random.NextDouble();
            // Now and then twice in a row.
            _blink = _random.Next(4) == 0 ? 0.42f : 0.14f;
        }
        _blink = MathF.Max(0f, _blink - dt);
        bool closing = _blink > 0f && (_blink < 0.14f || _blink > 0.28f);
        float content = Petted || _emotion == Emotion.Happy ? 0.5f : _emotion == Emotion.Relaxed ? 0.35f : _emotion is Emotion.Sad or Emotion.Worried ? 0.25f : 0f;
        string? activity = _activity;
        if (activity is "eat" or "bone")
            content = MathF.Max(content, 0.45f);
        if (activity == "wash_face")
            content = 0.85f;
        float open = (1f - content) * (1f - MathF.Max(_lie, _sprawl * 0.6f));
        bool play = activity is "yarn" or "ball";
        bool startled = _emotion == Emotion.Surprised || _gesture?.Gesture == Gesture.Startle || _fall > 0.5f;
        _eyes.Pose(dt, closing ? 0f : open, startled ? 1f : play ? 0.4f : 0f, startled || play ? 1f : 0f);
    }

    private void PoseBones(float dt, float speedMetres, float climbSpeedMetres, ClimbGrip grip)
    {
        var rig = _rig!;
        if (_act < 0.01f)
            _fadingActivity = null;
        _gaitSpeed = MathF.Abs(speedMetres);
        rig.Clear();
        BonesIdle(rig, dt);
        BonesTurn(rig, dt);
        BonesWalk(rig, dt);
        BonesDance(rig);
        BonesSit(rig);
        BonesLie(rig);
        BonesGetUp(rig);
        BonesAir(rig);
        BonesClimb(rig, dt, climbSpeedMetres, grip);
        BonesActivity(rig);
        BonesGesture(rig);
        BonesMood(rig);
        BonesLook(rig);
        rig.Apply(dt, _size.Y);
        PoseEyes(dt);
    }

    /// <summary>How much it has its paws on the ground.</summary>
    private float Grounded => 1f - MathF.Max(_climb, MathF.Max(_fall, MathF.Max(_drag, MathF.Max(_jump, MathF.Max(_sprawl, _tumble)))));

    /// <summary>How much it is just standing about.</summary>
    private float Calm => MathF.Max(0f, Grounded - _walk);

    /// <summary>A soft stance, breathing, a slow shift of weight, glances around, a tail with moods of its own.</summary>
    private void BonesIdle(ModelAnimalRig rig, float dt)
    {
        float still = Calm;
        float breath = MathF.Sin(_time * Mathf.Tau / 2.8f);
        // Knees a little bent and the head carried forward, never standing like a table.
        rig.AddLegs(Ch.Plant0, Grounded);
        rig.Add(Ch.HipsY, -0.025f * Grounded);
        rig.Add(Ch.NeckPitch, 0.14f * Grounded);
        rig.Add(Ch.HeadPitch, -0.1f * Grounded);
        rig.Add(Ch.ChestPitch, 0.025f * breath);
        rig.Add(Ch.HipsY, 0.003f * breath);
        rig.Add(Ch.HeadPitch, -0.03f * breath);
        rig.Add(Ch.HipsZ, 0.012f * MathF.Sin(_time * 0.47f) * still);
        rig.Add(Ch.HipsRoll, 0.03f * MathF.Sin(_time * 0.6f) * still);
        rig.Add(Ch.ChestRoll, -0.035f * MathF.Sin(_time * 0.6f) * still);

        _nextGlance -= dt;
        if (_nextGlance <= 0f)
        {
            _nextGlance = 2.5f + 5f * (float)_random.NextDouble();
            _glanceHold = 0.8f + 1.2f * (float)_random.NextDouble();
            _glance = (_random.Next(2) == 0 ? -1f : 1f) * (0.3f + 0.5f * (float)_random.NextDouble());
        }
        _glanceHold -= dt;
        float glance = _glanceHold > 0f ? _glance * still * (Talking ? 0f : 1f) : 0f;
        rig.Add(Ch.NeckYaw, 0.4f * glance);
        rig.Add(Ch.HeadYaw, 0.6f * glance);

        // The tail: low most of the time, up now and then, the tip flicking on its own.
        _tailMoodTimer -= dt;
        if (_tailMoodTimer <= 0f)
        {
            _tailMoodTimer = 5f + 10f * (float)_random.NextDouble();
            _tailMood = _random.NextDouble() < 0.18 ? 0.3f : 0.75f + 0.25f * (float)_random.NextDouble();
        }
        rig.Add(Ch.TailRelax, _tailMood * still + (1f - still));
        rig.Add(Ch.TailSpeed, 1.1f + 1.6f * _walk + 3f * _run);
        rig.Add(Ch.TailWag, 0.22f + 0.1f * _walk);
        _nextFlick -= dt;
        if (_nextFlick <= 0f)
        {
            _nextFlick = 2f + 6f * (float)_random.NextDouble();
            _flick = 0.5f;
        }
        _flick = MathF.Max(0f, _flick - dt);
        rig.Add(Ch.TailFlick, 0.55f * MathF.Sin(_flick * 28f) * Smooth(Mathf.Clamp(_flick * 4f, 0f, 1f)));

        if (Talking)
        {
            rig.Add(Ch.HeadPitch, 0.07f * MathF.Sin(_time * 9f));
            rig.Add(Ch.HeadRoll, 0.06f * MathF.Sin(_time * 1.7f));
        }
        if (Petted)
        {
            // Pushing into the hand, tail straight up and hooked.
            rig.Add(Ch.NeckPitch, -0.15f);
            rig.Add(Ch.HeadRoll, 0.25f * MathF.Sin(_time * 3.1f));
            rig.Add(Ch.TailRelax, -0.85f);
            rig.Add(Ch.TailTip, 0.5f);
            rig.Add(Ch.ChestRoll, 0.08f * MathF.Sin(_time * 3.1f));
        }
    }

    /// <summary>Turning: the head goes first, the body follows, the tail swings out behind.</summary>
    private void BonesTurn(ModelAnimalRig rig, float dt)
    {
        var forward = _root.GlobalBasis.Z;
        float yaw = MathF.Atan2(forward.X, forward.Z);
        float rate = dt > 0f ? Mathf.AngleDifference(_lastYaw, yaw) / dt : 0f;
        _lastYaw = yaw;
        Ease(ref _turnBend, Mathf.Clamp(rate * 0.22f, -1f, 1f), dt, 10f);
        float bend = _turnBend * Grounded;
        rig.Add(Ch.HeadYaw, 0.35f * bend);
        rig.Add(Ch.NeckYaw, 0.3f * bend);
        rig.Add(Ch.ChestYaw, 0.2f * bend);
        rig.Add(Ch.HipsYaw, -0.12f * bend);
        rig.Add(Ch.TailSway, -0.8f * bend);
    }

    /// <summary>The gait worked out paw by paw (walk or bounding gallop), so no paw slides on the ground.</summary>
    private void BonesWalk(ModelAnimalRig rig, float dt)
    {
        float w = _walk;
        if (w <= 0.001f)
            return;
        float stride = Mathf.Lerp(WalkStride, GallopStride, _run);
        float duty = Mathf.Lerp(WalkDuty, GallopDuty, _run);
        _gait = (_gait + dt * _gaitSpeed / stride) % 1f;
        float sweep = stride * duty / _size.Y;
        float lift = Mathf.Lerp(0.07f, 0.12f, _run);
        for (int i = 0; i < 4; i++)
        {
            float p = (_gait + Mathf.Lerp(WalkOffsets[i], GallopOffsets[i], _run)) % 1f;
            float z, y = 0f, flex = 0f;
            if (p < duty)
            {
                // On the ground: staying where it was put as the body goes over it.
                z = sweep * (0.5f - p / duty);
            }
            else
            {
                // Lifted and brought forward, the paw hanging down from the wrist.
                float s = (p - duty) / (1f - duty);
                z = sweep * (Smooth(s) - 0.5f);
                y = lift * MathF.Sin(MathF.PI * s);
                flex = 0.9f * MathF.Sin(MathF.PI * s);
            }
            rig.AddLeg(Ch.FootZ0, i, z * w);
            rig.AddLeg(Ch.FootY0, i, y * w);
            rig.AddLeg(Ch.Flex0, i, flex * w);
        }

        float φ = _gait * Mathf.Tau;
        float k = w * (1f - _run);
        // A wave along the spine: shoulders and hips swing and roll against each other.
        rig.Add(Ch.HipsY, (-0.03f + 0.007f * MathF.Cos(2f * φ)) * k);
        rig.Add(Ch.HipsYaw, 0.09f * MathF.Sin(φ) * k);
        rig.Add(Ch.ChestYaw, -0.17f * MathF.Sin(φ + 0.5f) * k);
        rig.Add(Ch.HipsRoll, 0.06f * MathF.Sin(φ + 1.2f) * k);
        rig.Add(Ch.ChestRoll, -0.09f * MathF.Sin(φ + 1.7f) * k);
        // Head low and level, hardly moved by all that: it swings against the shoulders.
        rig.Add(Ch.NeckPitch, 0.14f * k);
        rig.Add(Ch.HeadPitch, -0.1f * k);
        rig.Add(Ch.NeckYaw, 0.1f * MathF.Sin(φ + 0.5f) * k);
        rig.Add(Ch.HeadYaw, 0.06f * MathF.Sin(φ + 0.5f) * k);
        rig.Add(Ch.HeadPitch, 0.025f * MathF.Cos(2f * φ) * k);

        // Running: the back folds and stretches with each bound, the tail streams out.
        float r = _run * w;
        float flexing = MathF.Sin(φ);
        rig.Add(Ch.HipsPitch, 0.2f * flexing * r);
        rig.Add(Ch.ChestPitch, -0.32f * flexing * r);
        rig.Add(Ch.HipsY, (0.015f + 0.03f * MathF.Abs(MathF.Cos(φ))) * r);
        rig.Add(Ch.NeckPitch, 0.2f * r);
        rig.Add(Ch.HeadPitch, 0.18f * flexing * r);
        rig.Add(Ch.TailTip, -0.5f * r);
        rig.Add(Ch.TailLift, 0.25f * flexing * r);
    }

    /// <summary>Bobbing and swaying on the spot, paws stepping in turn.</summary>
    private void BonesDance(ModelAnimalRig rig)
    {
        float w = _dance;
        if (w <= 0.001f)
            return;
        float b = _time * Mathf.Tau * 0.9f;
        rig.Add(Ch.HipsY, -0.03f * (0.5f - 0.5f * MathF.Cos(2f * b)) * w);
        rig.Add(Ch.HipsRoll, 0.1f * MathF.Sin(b) * w);
        rig.Add(Ch.ChestRoll, -0.14f * MathF.Sin(b) * w);
        rig.Add(Ch.HeadRoll, 0.18f * MathF.Sin(b) * w);
        rig.Add(Ch.NeckYaw, 0.25f * MathF.Sin(b) * w);
        for (int i = 0; i < 4; i++)
            rig.AddLeg(Ch.FootY0, i, 0.05f * MathF.Max(0f, MathF.Sin(b + i * MathF.PI / 2f)) * w);
        rig.Add(Ch.TailRelax, -0.6f * w);
        rig.Add(Ch.TailWag, 0.5f * w);
        rig.Add(Ch.TailSpeed, 6f * w);
    }

    /// <summary>On its haunches: rump down, back upright, front legs straight, tail along the ground.</summary>
    private void BonesSit(ModelAnimalRig rig)
    {
        float w = _sit;
        if (w <= 0.001f)
            return;
        rig.Add(Ch.HipsPitch, -0.45f * w);
        rig.Add(Ch.ChestPitch, -0.1f * w);
        rig.Add(Ch.HipsY, -0.2f * w);
        rig.Add(Ch.HipsZ, -0.03f * w);
        // The head stays level as the back rises.
        rig.Add(Ch.NeckPitch, 0.15f * w);
        rig.Add(Ch.HeadPitch, 0.3f * w);
        rig.AddLegs(Ch.Plant0, w);
        rig.AddLeg(Ch.FootZ0, 2, 0.14f * w);
        rig.AddLeg(Ch.FootZ0, 3, 0.14f * w);
        rig.AddLeg(Ch.FootZ0, 0, -0.03f * w);
        rig.AddLeg(Ch.FootZ0, 1, -0.03f * w);
        rig.Add(Ch.TailLift, -0.5f * w);
        rig.Add(Ch.TailSway, 0.7f * w);
    }

    /// <summary>Curled up asleep: belly down, paws tucked, head on them, tail round, slow breaths.</summary>
    private void BonesLie(ModelAnimalRig rig)
    {
        float w = _lie;
        if (w <= 0.001f)
            return;
        float breath = MathF.Sin(_time * Mathf.Tau / 4.5f);
        rig.Add(Ch.HipsY, (-0.21f + 0.006f * breath) * w);
        rig.Add(Ch.HipsRoll, _lieSide * 0.12f * w);
        rig.Add(Ch.ChestPitch, (0.06f + 0.02f * breath) * w);
        rig.AddLegs(Ch.Plant0, w);
        rig.AddLeg(Ch.FootZ0, 0, 0.05f * w);
        rig.AddLeg(Ch.FootZ0, 1, 0.05f * w);
        rig.AddLeg(Ch.FootZ0, 2, 0.13f * w);
        rig.AddLeg(Ch.FootZ0, 3, 0.13f * w);
        rig.Add(Ch.NeckPitch, 0.25f * w);
        rig.Add(Ch.HeadPitch, 0.3f * w);
        rig.Add(Ch.HeadRoll, _lieSide * 0.3f * w);
        rig.Add(Ch.TailLift, -0.6f * w);
        rig.Add(Ch.TailSway, _lieSide * 1.3f * w);
        rig.Add(Ch.TailWag, -0.15f * w);
    }

    /// <summary>Half up, then a shake from head to tail.</summary>
    private void BonesGetUp(ModelAnimalRig rig)
    {
        float w = _getUp;
        if (w <= 0.001f)
            return;
        float shake = MathF.Sin(_time * 30f) * Smooth(Mathf.Clamp((_stateTime - 0.35f) * 4f, 0f, 1f));
        rig.AddLegs(Ch.Plant0, w);
        rig.Add(Ch.HipsY, -0.06f * w);
        rig.Add(Ch.ChestRoll, 0.18f * shake * w);
        rig.Add(Ch.HipsRoll, -0.12f * shake * w);
        rig.Add(Ch.HeadRoll, -0.3f * shake * w);
    }

    /// <summary>Off the ground: carried by the scruff, falling spread out, jumping stretched, landing with a bounce.</summary>
    private void BonesAir(ModelAnimalRig rig)
    {
        if (_drag > 0.001f)
        {
            // Lifted by the scruff (the whole model hangs from the neck, see PoseWhole): the
            // head stays level, back slightly curled, all four legs hanging limp, the back
            // ones tucked up a little, the tail hanging and curling under.
            float sway = 0.1f * MathF.Sin(_time * 2.6f) + _dangle * 0.5f;
            rig.Add(Ch.NeckPitch, 0.75f * _drag);
            rig.Add(Ch.HeadPitch, 0.42f * _drag);
            rig.Add(Ch.ChestPitch, 0.12f * _drag);
            rig.Add(Ch.HipsPitch, 0.08f * _drag);
            rig.AddLeg(Ch.Swing0, 0, (0.95f + sway) * _drag);
            rig.AddLeg(Ch.Swing0, 1, (0.95f - sway) * _drag);
            rig.AddLeg(Ch.Knee0, 0, 0.25f * _drag);
            rig.AddLeg(Ch.Knee0, 1, 0.25f * _drag);
            rig.AddLeg(Ch.Swing0, 2, (0.7f + sway) * _drag);
            rig.AddLeg(Ch.Swing0, 3, (0.7f - sway) * _drag);
            rig.AddLeg(Ch.Knee0, 2, 0.55f * _drag);
            rig.AddLeg(Ch.Knee0, 3, 0.55f * _drag);
            rig.Add(Ch.TailRelax, _drag);
            rig.Add(Ch.TailTip, 0.7f * _drag);
            rig.Add(Ch.TailSway, 0.5f * sway * _drag);
        }
        if (_fall > 0.001f)
        {
            // Righting itself: the front half turns before the back, then all four legs
            // reach down for the ground, back arched, the tail swinging round for balance.
            float twist = MathF.Exp(-_stateTime * 4.5f);
            rig.Add(Ch.ChestRoll, -_lieSide * 0.9f * twist * _fall);
            rig.Add(Ch.HipsRoll, _lieSide * 0.4f * twist * _fall);
            rig.Add(Ch.TailSway, _lieSide * 1.5f * twist * _fall);
            rig.AddLeg(Ch.Swing0, 0, (-0.3f + 0.1f * MathF.Sin(_time * 9f)) * _fall);
            rig.AddLeg(Ch.Swing0, 1, (-0.3f + 0.1f * MathF.Sin(_time * 9f + 1.5f)) * _fall);
            rig.AddLeg(Ch.Swing0, 2, (0.25f + 0.1f * MathF.Sin(_time * 9f + 3f)) * _fall);
            rig.AddLeg(Ch.Swing0, 3, (0.25f + 0.1f * MathF.Sin(_time * 9f + 4.5f)) * _fall);
            rig.Add(Ch.ChestPitch, -0.12f * _fall);
            rig.Add(Ch.HeadPitch, 0.25f * _fall);
            rig.Add(Ch.TailRelax, 0.45f * _fall);
            rig.Add(Ch.TailWag, 0.5f * _fall);
            rig.Add(Ch.TailSpeed, 6f * _fall);
        }
        if (_jump > 0.001f)
        {
            // Front paws reaching forward, back legs pushed out behind, tail for balance.
            rig.AddLeg(Ch.Swing0, 0, -1.0f * _jump);
            rig.AddLeg(Ch.Swing0, 1, -1.0f * _jump);
            rig.AddLeg(Ch.Swing0, 2, 0.9f * _jump);
            rig.AddLeg(Ch.Swing0, 3, 0.9f * _jump);
            rig.Add(Ch.TailRelax, 0.2f * _jump);
        }
        if (_land > 0.001f)
        {
            // Absorbing the drop: chest low first, the legs folding like springs.
            rig.AddLegs(Ch.Plant0, _land);
            rig.Add(Ch.HipsY, -0.14f * _land);
            rig.Add(Ch.ChestPitch, 0.14f * _land);
            rig.Add(Ch.NeckPitch, -0.1f * _land);
            rig.Add(Ch.TailLift, 0.3f * _land);
        }
        if (_tumble > 0.001f)
        {
            // A hard landing: still on its feet but squashed low, legs splayed, head swimming.
            float wobble = MathF.Exp(-_stateTime * 1.2f);
            rig.AddLegs(Ch.Plant0, 2f * _tumble);
            rig.Add(Ch.HipsY, -0.17f * _tumble);
            rig.AddLeg(Ch.FootZ0, 0, 0.1f * _tumble);
            rig.AddLeg(Ch.FootZ0, 1, 0.1f * _tumble);
            rig.AddLeg(Ch.FootZ0, 2, -0.04f * _tumble);
            rig.AddLeg(Ch.FootZ0, 3, -0.04f * _tumble);
            rig.Add(Ch.ChestRoll, 0.12f * MathF.Sin(_time * 5f) * wobble * _tumble);
            rig.Add(Ch.HeadRoll, 0.45f * MathF.Sin(_time * 7f) * wobble * _tumble);
            rig.Add(Ch.NeckYaw, 0.25f * MathF.Sin(_time * 5f) * wobble * _tumble);
            rig.Add(Ch.NeckPitch, 0.2f * _tumble);
            rig.Add(Ch.TailRelax, _tumble);
        }
        if (_sprawl > 0.001f)
        {
            // Flat on its belly, legs out in front and behind, chin on the floor; a feeble kick after a moment.
            float kick = MathF.Max(0f, MathF.Sin(_stateTime * 8f)) * Smooth(Mathf.Clamp((_stateTime - 0.9f) / 0.4f, 0f, 1f)) * MathF.Exp(-MathF.Max(0f, _stateTime - 1.5f) * 2f);
            rig.Add(Ch.HipsY, -0.23f * _sprawl);
            rig.AddLeg(Ch.Swing0, 0, -1.25f * _sprawl);
            rig.AddLeg(Ch.Swing0, 1, -1.25f * _sprawl);
            rig.AddLeg(Ch.Swing0, 2, 1.25f * _sprawl);
            rig.AddLeg(Ch.Swing0, 3, (1.25f - 0.5f * kick) * _sprawl);
            rig.Add(Ch.NeckPitch, 0.5f * _sprawl);
            rig.Add(Ch.HeadPitch, 0.15f * _sprawl);
            rig.Add(Ch.TailRelax, _sprawl);
            rig.Add(Ch.TailLift, -0.3f * _sprawl);
        }
    }

    /// <summary>
    /// Going up a wall (the whole model is turned nose-up, the wall where the ground was):
    /// hugging it, not standing off it. The front paws reach up in turn and hook in, the
    /// back legs push together, so the body bunches up and stretches out with each heave;
    /// the head looks up the wall, the tail hangs and swings for balance.
    /// </summary>
    private void BonesClimb(ModelAnimalRig rig, float dt, float climbSpeedMetres, ClimbGrip grip)
    {
        float w = _climb;
        if (w <= 0.001f)
            return;
        float speed = grip == ClimbGrip.Slipping ? 0.9f : grip == ClimbGrip.Hanging ? 0f : MathF.Abs(climbSpeedMetres);
        _climbGait = (_climbGait + dt * speed / ClimbStride) % 1f;
        float sweep = ClimbStride * ClimbDuty / _size.Y;
        for (int i = 0; i < 4; i++)
        {
            // Hanging, only the front paws still hold on; the back legs dangle.
            float hold = i < 2 ? 1f : 1f - _hang;
            rig.AddLeg(Ch.Plant0, i, hold * w);
            float p = (_climbGait + ClimbOffsets[i]) % 1f;
            float z, y = 0f;
            if (p < ClimbDuty)
            {
                z = sweep * (0.5f - p / ClimbDuty);
            }
            else
            {
                float s = (p - ClimbDuty) / (1f - ClimbDuty);
                z = sweep * (Smooth(s) - 0.5f);
                y = 0.07f * MathF.Sin(MathF.PI * s);
            }
            // The front paws reach high, the back ones are drawn up under the body.
            z += i < 2 ? 0.1f : 0.07f;
            rig.AddLeg(Ch.FootZ0, i, z * hold * w);
            rig.AddLeg(Ch.FootY0, i, y * hold * w);
        }
        float heave = MathF.Sin(_climbGait * Mathf.Tau * 2f);
        float going = speed > 0.01f ? 1f : 0f;
        // Close to the wall, the back arching and flattening.
        rig.Add(Ch.HipsY, -0.19f * w);
        rig.Add(Ch.HipsPitch, 0.1f * heave * going * w);
        rig.Add(Ch.ChestPitch, (0.06f - 0.16f * heave * going) * w);
        rig.Add(Ch.HipsZ, 0.025f * heave * going * w);
        float rock = MathF.Sin(_climbGait * Mathf.Tau);
        rig.Add(Ch.ChestRoll, 0.14f * rock * going * w);
        rig.Add(Ch.HipsRoll, -0.09f * rock * going * w);
        rig.Add(Ch.HipsYaw, 0.06f * rock * going * w);
        rig.Add(Ch.ChestYaw, -0.1f * rock * going * w);
        // The neck reaches along the wall, the head tipped back to look up it.
        rig.Add(Ch.NeckPitch, 0.5f * w);
        rig.Add(Ch.HeadPitch, -0.3f * w);
        rig.Add(Ch.TailRelax, w);
        rig.Add(Ch.TailLift, -0.3f * w);
        rig.Add(Ch.TailWag, 0.3f * w);
        rig.Add(Ch.TailSpeed, 2.2f * w);
        if (_slip > 0.001f)
        {
            // Scrabbling, ears flat, sliding down.
            rig.Add(Ch.HipsY, 0.04f * _slip);
            rig.Add(Ch.TailRelax, -0.4f * _slip);
            rig.Add(Ch.TailWag, 0.6f * _slip);
            rig.Add(Ch.TailSpeed, 10f * _slip);
        }
        if (_hang > 0.001f)
        {
            // Hanging by the front paws: the body swings out, back legs paddling for a hold.
            float swing = MathF.Sin(_time * 3.4f);
            rig.AddLeg(Ch.Swing0, 2, (1.1f + 0.3f * swing) * _hang);
            rig.AddLeg(Ch.Swing0, 3, (1.1f - 0.3f * swing) * _hang);
            rig.Add(Ch.HipsY, 0.05f * _hang);
            rig.Add(Ch.TailSway, 0.5f * swing * _hang);
        }
    }

    private void BonesActivity(ModelAnimalRig rig)
    {
        float w = _act;
        string? activity = _activity ?? _fadingActivity;
        if (w <= 0.001f || activity == null)
            return;
        float t = _props.Time;
        switch (activity)
        {
            case "eat":
            {
                float eating = _props.FoodLeft > 0f ? 1f : 0.15f;
                float chew = MathF.Max(0f, MathF.Sin(t * 10f));
                rig.AddLegs(Ch.Plant0, w);
                // Shoulders dropped between the front legs, the muzzle down at the food.
                rig.Add(Ch.HipsPitch, 0.08f * w);
                rig.Add(Ch.ChestPitch, 0.2f * eating * w);
                rig.Add(Ch.HipsY, -0.02f * w);
                rig.Add(Ch.NeckPitch, (0.42f * eating - 0.05f) * w);
                rig.Add(Ch.HeadPitch, (0.06f * eating + 0.09f * chew * eating) * w);
                rig.Add(Ch.HeadRoll, 0.06f * MathF.Sin(t * 2.3f) * w);
                rig.Add(Ch.TailWag, 0.15f * w);
                break;
            }
            case "yarn":
            case "ball":
            {
                // A play bow: chest down, rump up, wiggling; a paw bats when the ball comes close.
                var ball = _props.BallPosition;
                float near = BallNear();
                float look = Mathf.Atan2(ball.X, MathF.Max(ball.Z, 0.01f));
                float swat = near * MathF.Max(0f, MathF.Sin(t * 9f));
                int paw = MathF.Sin(t * 1.1f) > 0f ? 0 : 1;
                rig.AddLegs(Ch.Plant0, w);
                rig.Add(Ch.HipsPitch, 0.16f * w);
                rig.Add(Ch.ChestPitch, 0.1f * w);
                rig.Add(Ch.HipsY, -0.05f * w);
                rig.Add(Ch.HipsRoll, 0.07f * MathF.Sin(t * 8f) * (1f - near) * w);
                rig.Add(Ch.NeckYaw, 0.4f * look * w);
                rig.Add(Ch.HeadYaw, 0.6f * look * w);
                rig.Add(Ch.NeckPitch, 0.15f * w);
                rig.Add(Ch.HeadPitch, 0.2f * w);
                rig.AddLeg(Ch.FootY0, paw, 0.14f * swat * w);
                rig.AddLeg(Ch.FootZ0, paw, 0.2f * swat * w);
                rig.Add(Ch.TailRelax, -0.4f * w);
                rig.Add(Ch.TailWag, 0.5f * w);
                rig.Add(Ch.TailSpeed, 5f * w);
                break;
            }
            case "wash_face":
            {
                // Sitting up: a paw to the mouth, licked, then wiped over the face.
                float cycle = (t * 0.55f) % 1f;
                float wipe = Smooth(Mathf.Clamp((cycle - 0.45f) * 6f, 0f, 1f)) * Smooth(Mathf.Clamp((1f - cycle) * 6f, 0f, 1f));
                rig.Add(Ch.HipsPitch, -0.45f * w);
                rig.Add(Ch.ChestPitch, -0.1f * w);
                rig.Add(Ch.HipsY, -0.2f * w);
                rig.Add(Ch.HipsZ, -0.03f * w);
                rig.AddLegs(Ch.Plant0, w);
                rig.AddLeg(Ch.FootZ0, 2, 0.14f * w);
                rig.AddLeg(Ch.FootZ0, 3, 0.14f * w);
                rig.AddLeg(Ch.FootZ0, 1, -0.03f * w);
                rig.AddLeg(Ch.FootY0, 0, (0.58f + 0.08f * wipe) * w);
                rig.AddLeg(Ch.FootZ0, 0, 0.16f * w);
                rig.Add(Ch.NeckPitch, 0.2f * w);
                rig.Add(Ch.HeadPitch, (0.55f + 0.1f * MathF.Sin(t * 11f) * (1f - wipe)) * w);
                rig.Add(Ch.HeadRoll, (0.25f * MathF.Sin(t * 6f) * wipe) * w);
                rig.Add(Ch.HeadYaw, 0.2f * w);
                rig.Add(Ch.TailLift, -0.5f * w);
                rig.Add(Ch.TailSway, 0.5f * w);
                break;
            }
            case "bone":
            {
                // Lying with the bone between the paws, gnawing with the head on one side.
                float gnaw = MathF.Sin(t * 8f);
                rig.Add(Ch.HipsY, -0.21f * w);
                rig.AddLegs(Ch.Plant0, w);
                rig.AddLeg(Ch.FootZ0, 0, 0.14f * w);
                rig.AddLeg(Ch.FootZ0, 1, 0.14f * w);
                rig.AddLeg(Ch.FootZ0, 2, 0.1f * w);
                rig.AddLeg(Ch.FootZ0, 3, 0.1f * w);
                rig.Add(Ch.NeckPitch, 0.7f * w);
                rig.Add(Ch.HeadPitch, (0.45f + 0.08f * gnaw) * w);
                rig.Add(Ch.HeadRoll, (0.4f + 0.12f * MathF.Sin(t * 1.3f)) * w);
                rig.Add(Ch.TailWag, 0.6f * w);
                rig.Add(Ch.TailSpeed, 6f * w);
                break;
            }
            case "roll":
                for (int i = 0; i < 4; i++)
                    rig.AddLeg(Ch.Swing0, i, 0.7f * MathF.Sin(t * 8f + i * 1.7f) * w);
                rig.Add(Ch.HeadRoll, -0.35f * MathF.Sin(t * 1.6f) * w);
                break;
        }
    }

    /// <summary>The animal versions of the doll's gestures, which these models have no clips for.</summary>
    private void BonesGesture(ModelAnimalRig rig)
    {
        if (_gesture == null)
            return;
        float u = Mathf.Clamp(_gestureTime / _gesture.Duration, 0f, 1f);
        float w = MathF.Sin(MathF.PI * u) * Mathf.Clamp(_gesture.Amplitude + 0.3f, 0.3f, 1.2f);
        float t = _gestureTime;
        switch (_gesture.Gesture)
        {
            case Gesture.Nod:
            case Gesture.Bow:
            case Gesture.Explain:
            case Gesture.Count:
                rig.Add(Ch.NeckPitch, 0.2f * MathF.Max(0f, MathF.Sin(t * 8f)) * w);
                rig.Add(Ch.HeadPitch, 0.25f * MathF.Max(0f, MathF.Sin(t * 8f)) * w);
                break;
            case Gesture.Shake:
            case Gesture.Pout:
                rig.Add(Ch.NeckYaw, 0.3f * MathF.Sin(t * 11f) * w);
                rig.Add(Ch.HeadYaw, 0.45f * MathF.Sin(t * 11f) * w);
                break;
            case Gesture.Tilt:
            case Gesture.Shrug:
            case Gesture.Think:
                rig.Add(Ch.HeadRoll, 0.4f * w);
                rig.Add(Ch.NeckYaw, 0.15f * w);
                break;
            case Gesture.Hop:
            case Gesture.Cheer:
            case Gesture.Clap:
            case Gesture.FistPump:
            case Gesture.ThumbsUp:
            {
                float hop = MathF.Abs(MathF.Sin(t * 7f));
                rig.Add(Ch.HipsY, 0.05f * hop * w);
                rig.Add(Ch.ChestPitch, -0.15f * hop * w);
                rig.Add(Ch.TailRelax, -0.6f * w);
                rig.Add(Ch.TailWag, 0.5f * w);
                rig.Add(Ch.TailSpeed, 8f * w);
                break;
            }
            case Gesture.Wave:
            case Gesture.Point:
            case Gesture.Idea:
                rig.AddLeg(Ch.FootY0, 0, (0.2f + 0.05f * MathF.Sin(t * 9f)) * w);
                rig.AddLeg(Ch.FootZ0, 0, 0.12f * w);
                rig.Add(Ch.HipsRoll, -0.08f * w);
                break;
            case Gesture.Stretch:
                // Front paws far ahead, rump up, chest to the floor.
                rig.Add(Ch.HipsPitch, 0.3f * w);
                rig.Add(Ch.ChestPitch, 0.16f * w);
                rig.AddLeg(Ch.FootZ0, 0, 0.26f * w);
                rig.AddLeg(Ch.FootZ0, 1, 0.26f * w);
                rig.Add(Ch.NeckPitch, -0.3f * w);
                rig.Add(Ch.TailRelax, -0.5f * w);
                break;
            case Gesture.LookAround:
                rig.Add(Ch.NeckYaw, 0.35f * MathF.Sin(u * Mathf.Tau) * w);
                rig.Add(Ch.HeadYaw, 0.5f * MathF.Sin(u * Mathf.Tau) * w);
                break;
            case Gesture.FixHair:
                rig.Add(Ch.NeckYaw, 0.6f * w);
                rig.Add(Ch.NeckPitch, 0.35f * w);
                rig.Add(Ch.HeadPitch, (0.2f + 0.08f * MathF.Sin(t * 10f)) * w);
                break;
            case Gesture.Yawn:
                rig.Add(Ch.NeckPitch, -0.2f * w);
                rig.Add(Ch.HeadPitch, -0.3f * w);
                break;
            case Gesture.Startle:
                rig.Add(Ch.HipsY, 0.04f * w);
                rig.Add(Ch.ChestPitch, -0.22f * w);
                rig.Add(Ch.NeckPitch, -0.15f * w);
                rig.Add(Ch.TailRelax, -0.7f * w);
                break;
            case Gesture.Droop:
            case Gesture.Sigh:
            case Gesture.Facepalm:
            case Gesture.Shy:
                rig.Add(Ch.NeckPitch, 0.35f * w);
                rig.Add(Ch.HeadPitch, 0.2f * w);
                rig.Add(Ch.TailLift, -0.4f * w);
                break;
        }
    }

    private void BonesMood(ModelAnimalRig rig)
    {
        switch (_emotion)
        {
            case Emotion.Happy:
                // The friendly tail: straight up, hooked at the tip like a question mark.
                rig.Add(Ch.TailRelax, -0.9f);
                rig.Add(Ch.TailTip, 0.55f);
                rig.Add(Ch.TailWag, 0.12f);
                rig.Add(Ch.HeadRoll, 0.07f * MathF.Sin(_time * 2.6f));
                break;
            case Emotion.Sad:
            case Emotion.Worried:
                // Low and tucked.
                rig.Add(Ch.NeckPitch, 0.25f);
                rig.Add(Ch.HeadPitch, 0.15f);
                rig.Add(Ch.TailLift, -0.7f);
                rig.Add(Ch.TailTip, -0.4f);
                break;
            case Emotion.Angry:
                // An arched back, the tail lashing.
                rig.Add(Ch.ChestPitch, -0.06f);
                rig.Add(Ch.NeckPitch, 0.15f);
                rig.Add(Ch.TailWag, 0.9f);
                rig.Add(Ch.TailSpeed, 8f);
                break;
            case Emotion.Surprised:
                rig.Add(Ch.NeckPitch, -0.2f);
                rig.Add(Ch.TailRelax, -0.7f);
                break;
            case Emotion.Shy:
                rig.Add(Ch.NeckPitch, 0.2f);
                rig.Add(Ch.HeadYaw, 0.3f);
                break;
        }
    }

    /// <summary>Following the mouse with the head (and a little with the neck), as the model no longer turns as one.</summary>
    private void BonesLook(ModelAnimalRig rig)
    {
        float w = 1f - MathF.Max(_lie, MathF.Max(_sprawl, _drag));
        rig.Add(Ch.NeckYaw, 0.35f * _lookYaw * w);
        rig.Add(Ch.HeadYaw, 0.65f * _lookYaw * w);
        rig.Add(Ch.NeckPitch, -0.25f * _lookPitch * w);
        rig.Add(Ch.HeadPitch, -0.45f * _lookPitch * w);
    }

    private static float Smooth(float x) => x * x * (3f - 2f * x);
}
