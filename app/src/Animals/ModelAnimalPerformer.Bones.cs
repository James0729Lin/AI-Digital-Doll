using System;
using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Mascot;
using Godot;
using Ch = DigitalDoll.Animals.ModelAnimalRig.Channel;

namespace DigitalDoll.Animals;

/// <summary>
/// What the model animals with a skeleton do with it: each behaviour adds to the rig's
/// channels with its weight, so sitting, lying, looking, moods, gestures and activities
/// mix and flow into each other instead of tilting the model as one block.
/// </summary>
public sealed partial class ModelAnimalPerformer
{
    private ModelAnimalRig? _rig;
    private float _walk, _run, _lookPitch, _nextGlance = 3f, _glance, _glanceHold;
    private string? _fadingActivity;

    private void PoseBones(float dt)
    {
        var rig = _rig!;
        if (_act < 0.01f)
            _fadingActivity = null;
        rig.Clear();
        BonesIdle(rig, dt);
        BonesWalk(rig);
        BonesSit(rig);
        BonesLie(rig);
        BonesGetUp(rig);
        BonesAir(rig);
        BonesClimb(rig);
        BonesActivity(rig);
        BonesGesture(rig);
        BonesMood(rig);
        BonesLook(rig);
        rig.Apply(dt, _size.Y);
    }

    /// <summary>Breathing, a slow shift of weight, an idle tail; now and then a glance around.</summary>
    private void BonesIdle(ModelAnimalRig rig, float dt)
    {
        float still = Calm;
        float breath = MathF.Sin(_time * Mathf.Tau / 2.8f);
        rig.Add(Ch.ChestPitch, 0.025f * breath);
        rig.Add(Ch.HipsY, 0.004f * breath);
        rig.Add(Ch.HeadPitch, -0.03f * breath);
        rig.Add(Ch.HipsRoll, 0.025f * MathF.Sin(_time * 0.6f) * still);
        rig.Add(Ch.ChestRoll, -0.03f * MathF.Sin(_time * 0.6f) * still);

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

        // A tail is never quite still; it sways slowly at rest and swishes when going.
        rig.Add(Ch.TailSpeed, 1.5f + 3.5f * _walk + 3f * _run);
        rig.Add(Ch.TailWag, 0.45f + 0.15f * _walk);
        rig.Add(Ch.TailLift, 0.15f * MathF.Sin(_time * 0.8f) * still);
        if (Talking)
        {
            rig.Add(Ch.HeadPitch, 0.07f * MathF.Sin(_time * 9f));
            rig.Add(Ch.HeadRoll, 0.06f * MathF.Sin(_time * 1.7f));
        }
        if (Petted)
        {
            // Pushing into the hand.
            rig.Add(Ch.NeckPitch, -0.15f);
            rig.Add(Ch.HeadRoll, 0.25f * MathF.Sin(_time * 3.1f));
            rig.Add(Ch.TailLift, 0.3f);
            rig.Add(Ch.ChestRoll, 0.08f * MathF.Sin(_time * 3.1f));
        }
    }

    /// <summary>How much it is just standing about.</summary>
    private float Calm => MathF.Max(0f, 1f - MathF.Max(_walk, MathF.Max(_climb, MathF.Max(_fall, MathF.Max(_drag, _jump)))));

    /// <summary>On top of the walk clip: the body rocks with the steps, the head stays steadier; a run bounds.</summary>
    private void BonesWalk(ModelAnimalRig rig)
    {
        float w = _walk;
        if (w <= 0.001f)
            return;
        float φ = _stepPhase;
        rig.Add(Ch.HipsY, 0.012f * MathF.Cos(2f * φ) * w);
        rig.Add(Ch.HipsRoll, 0.05f * MathF.Sin(φ) * w);
        rig.Add(Ch.ChestRoll, -0.07f * MathF.Sin(φ) * w);
        rig.Add(Ch.HipsYaw, 0.04f * MathF.Sin(φ) * w);
        rig.Add(Ch.ChestYaw, -0.08f * MathF.Sin(φ) * w);
        rig.Add(Ch.HeadPitch, -0.04f * MathF.Cos(2f * φ) * w);
        rig.Add(Ch.HeadRoll, 0.05f * MathF.Sin(φ) * w);
        float r = _run * w;
        // The back flexes and stretches, head low and forward, tail streaming behind.
        rig.Add(Ch.HipsPitch, 0.12f * MathF.Sin(2f * φ) * r);
        rig.Add(Ch.ChestPitch, -0.2f * MathF.Sin(2f * φ) * r);
        rig.Add(Ch.HipsY, 0.04f * MathF.Abs(MathF.Sin(2f * φ)) * r);
        rig.Add(Ch.NeckPitch, 0.2f * r);
        rig.Add(Ch.TailLift, -0.7f * r);
    }

    /// <summary>On its haunches: rump down, back upright, front legs straight, tail around.</summary>
    private void BonesSit(ModelAnimalRig rig)
    {
        float w = _sit;
        if (w <= 0.001f)
            return;
        rig.Add(Ch.HipsPitch, -0.45f * w);
        rig.Add(Ch.ChestPitch, -0.1f * w);
        rig.Add(Ch.HipsY, -0.22f * w);
        rig.Add(Ch.HipsZ, -0.03f * w);
        // The head stays level as the back rises.
        rig.Add(Ch.NeckPitch, 0.25f * w);
        rig.Add(Ch.HeadPitch, 0.3f * w);
        rig.AddLegs(Ch.Plant0, w);
        rig.AddLeg(Ch.FootZ0, 2, 0.14f * w);
        rig.AddLeg(Ch.FootZ0, 3, 0.14f * w);
        rig.AddLeg(Ch.FootZ0, 0, -0.03f * w);
        rig.AddLeg(Ch.FootZ0, 1, -0.03f * w);
        rig.Add(Ch.TailLift, -2.1f * w);
        rig.Add(Ch.TailSway, 0.8f * w);
    }

    /// <summary>Curled up asleep: belly down, paws tucked, head on them, tail round, slow breaths.</summary>
    private void BonesLie(ModelAnimalRig rig)
    {
        float w = _lie;
        if (w <= 0.001f)
            return;
        float breath = MathF.Sin(_time * Mathf.Tau / 4.5f);
        rig.Add(Ch.HipsY, (-0.235f + 0.006f * breath) * w);
        rig.Add(Ch.HipsRoll, _lieSide * 0.12f * w);
        rig.Add(Ch.ChestPitch, (0.06f + 0.02f * breath) * w);
        rig.AddLegs(Ch.Plant0, w);
        rig.AddLeg(Ch.FootZ0, 0, 0.05f * w);
        rig.AddLeg(Ch.FootZ0, 1, 0.05f * w);
        rig.AddLeg(Ch.FootZ0, 2, 0.13f * w);
        rig.AddLeg(Ch.FootZ0, 3, 0.13f * w);
        rig.Add(Ch.NeckPitch, 0.35f * w);
        rig.Add(Ch.HeadPitch, 0.3f * w);
        rig.Add(Ch.HeadRoll, _lieSide * 0.3f * w);
        rig.Add(Ch.TailLift, -2.3f * w);
        rig.Add(Ch.TailSway, _lieSide * 1.1f * w);
        rig.Add(Ch.TailWag, -0.35f * w);
    }

    /// <summary>Half up, then a shake from head to tail.</summary>
    private void BonesGetUp(ModelAnimalRig rig)
    {
        float w = _getUp;
        if (w <= 0.001f)
            return;
        float shake = MathF.Sin(_time * 30f) * Smooth(Mathf.Clamp((_stateTime - 0.35f) * 4f, 0f, 1f));
        rig.AddLegs(Ch.Plant0, w);
        rig.Add(Ch.HipsY, -0.08f * w);
        rig.Add(Ch.ChestRoll, 0.18f * shake * w);
        rig.Add(Ch.HipsRoll, -0.12f * shake * w);
        rig.Add(Ch.HeadRoll, -0.3f * shake * w);
    }

    /// <summary>Off the ground: carried by the scruff, falling spread out, jumping stretched, landing with a bounce.</summary>
    private void BonesAir(ModelAnimalRig rig)
    {
        if (_drag > 0.001f)
        {
            // Hanging limp at the back, front paws paddling, tail tucked.
            rig.Add(Ch.TailLift, -1.5f * _drag);
            rig.AddLeg(Ch.Swing0, 0, (-0.3f + 0.35f * MathF.Sin(_time * 7f)) * _drag);
            rig.AddLeg(Ch.Swing0, 1, (-0.3f + 0.35f * MathF.Sin(_time * 7f + 2f)) * _drag);
            rig.AddLeg(Ch.Swing0, 2, (0.2f + _dangle * 0.6f) * _drag);
            rig.AddLeg(Ch.Swing0, 3, (0.2f + _dangle * 0.6f) * _drag);
            rig.Add(Ch.HeadPitch, -0.2f * _drag);
        }
        if (_fall > 0.001f)
        {
            rig.AddLeg(Ch.Swing0, 0, -0.7f * _fall);
            rig.AddLeg(Ch.Swing0, 1, -0.7f * _fall);
            rig.AddLeg(Ch.Swing0, 2, 0.6f * _fall);
            rig.AddLeg(Ch.Swing0, 3, 0.6f * _fall);
            rig.Add(Ch.TailLift, 0.6f * _fall);
            rig.Add(Ch.TailWag, 0.4f * _fall);
            rig.Add(Ch.HeadPitch, -0.2f * _fall);
        }
        if (_jump > 0.001f)
        {
            // Front paws reaching forward, back legs pushed out behind, tail for balance.
            rig.AddLeg(Ch.Swing0, 0, -1.0f * _jump);
            rig.AddLeg(Ch.Swing0, 1, -1.0f * _jump);
            rig.AddLeg(Ch.Swing0, 2, 0.9f * _jump);
            rig.AddLeg(Ch.Swing0, 3, 0.9f * _jump);
            rig.Add(Ch.TailLift, -0.7f * _jump);
        }
        if (_land > 0.001f)
        {
            rig.AddLegs(Ch.Plant0, _land);
            rig.Add(Ch.HipsY, -0.14f * _land);
            rig.Add(Ch.ChestPitch, 0.12f * _land);
            rig.Add(Ch.HeadPitch, -0.15f * _land);
        }
        if (_tumble > 0.001f)
        {
            float wobble = MathF.Exp(-_stateTime * 1.2f);
            rig.Add(Ch.HeadRoll, 0.45f * MathF.Sin(_time * 7f) * wobble * _tumble);
            rig.Add(Ch.NeckYaw, 0.25f * MathF.Sin(_time * 5f) * wobble * _tumble);
        }
        if (_sprawl > 0.001f)
        {
            rig.AddLeg(Ch.Swing0, 0, -1.2f * _sprawl);
            rig.AddLeg(Ch.Swing0, 1, -1.2f * _sprawl);
            rig.AddLeg(Ch.Swing0, 2, 1.2f * _sprawl);
            rig.AddLeg(Ch.Swing0, 3, 1.2f * _sprawl);
            rig.Add(Ch.TailLift, -1f * _sprawl);
        }
    }

    /// <summary>Going up a wall (the whole model is turned nose-up): looking up, tail hanging down.</summary>
    private void BonesClimb(ModelAnimalRig rig)
    {
        float w = _climb;
        if (w <= 0.001f)
            return;
        rig.Add(Ch.HeadPitch, -0.35f * w);
        rig.Add(Ch.TailLift, -1.2f * w);
        rig.Add(Ch.ChestPitch, 0.1f * MathF.Sin(_time * 6f) * w * (1f - _hang));
        rig.Add(Ch.TailWag, 0.3f * w);
        if (_slip > 0.001f)
        {
            rig.AddLeg(Ch.Swing0, 0, -0.7f * _slip);
            rig.AddLeg(Ch.Swing0, 1, -0.7f * _slip);
        }
        if (_hang > 0.001f)
        {
            rig.AddLeg(Ch.Swing0, 0, -1.4f * _hang);
            rig.AddLeg(Ch.Swing0, 1, -1.4f * _hang);
            rig.AddLeg(Ch.Swing0, 2, 0.35f * MathF.Sin(_time * 3.4f) * _hang);
            rig.AddLeg(Ch.Swing0, 3, -0.35f * MathF.Sin(_time * 3.4f) * _hang);
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
                rig.Add(Ch.HipsPitch, 0.1f * w);
                rig.Add(Ch.ChestPitch, 0.22f * eating * w);
                rig.Add(Ch.HipsY, -0.03f * w);
                rig.Add(Ch.NeckPitch, (0.55f * eating - 0.05f) * w);
                rig.Add(Ch.HeadPitch, (0.3f * eating + 0.07f * chew * eating) * w);
                rig.Add(Ch.HeadRoll, 0.06f * MathF.Sin(t * 2.3f) * w);
                rig.Add(Ch.TailWag, 0.3f * w);
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
                rig.Add(Ch.TailLift, 0.3f * w);
                rig.Add(Ch.TailWag, 0.5f * w);
                break;
            }
            case "wash_face":
            {
                // Sitting up (the sit behaviour's pose): a paw to the mouth, licked, then wiped over the face.
                float cycle = (t * 0.55f) % 1f;
                float wipe = Smooth(Mathf.Clamp((cycle - 0.45f) * 6f, 0f, 1f)) * Smooth(Mathf.Clamp((1f - cycle) * 6f, 0f, 1f));
                rig.Add(Ch.HipsPitch, -0.45f * w);
                rig.Add(Ch.ChestPitch, -0.1f * w);
                rig.Add(Ch.HipsY, -0.22f * w);
                rig.Add(Ch.HipsZ, -0.03f * w);
                rig.AddLegs(Ch.Plant0, w);
                rig.AddLeg(Ch.FootZ0, 2, 0.14f * w);
                rig.AddLeg(Ch.FootZ0, 3, 0.14f * w);
                rig.AddLeg(Ch.FootZ0, 1, -0.03f * w);
                rig.AddLeg(Ch.FootY0, 0, (0.58f + 0.08f * wipe) * w);
                rig.AddLeg(Ch.FootZ0, 0, 0.16f * w);
                rig.Add(Ch.NeckPitch, 0.3f * w);
                rig.Add(Ch.HeadPitch, (0.55f + 0.1f * MathF.Sin(t * 11f) * (1f - wipe)) * w);
                rig.Add(Ch.HeadRoll, (0.25f * MathF.Sin(t * 6f) * wipe) * w);
                rig.Add(Ch.HeadYaw, 0.2f * w);
                rig.Add(Ch.TailLift, -2.1f * w);
                rig.Add(Ch.TailSway, 0.6f * w);
                break;
            }
            case "bone":
            {
                // Lying with the bone between the paws, gnawing with the head on one side.
                float gnaw = MathF.Sin(t * 8f);
                rig.Add(Ch.HipsY, -0.235f * w);
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
                rig.AddLegs(Ch.Plant0, 0.6f * w);
                rig.Add(Ch.HipsY, 0.05f * hop * w);
                rig.Add(Ch.ChestPitch, -0.15f * hop * w);
                rig.Add(Ch.TailWag, 0.6f * w);
                rig.Add(Ch.TailSpeed, 8f * w);
                break;
            }
            case Gesture.Wave:
            case Gesture.Point:
            case Gesture.Idea:
                rig.AddLeg(Ch.Swing0, 0, (-1.1f + 0.3f * MathF.Sin(t * 9f)) * w);
                rig.Add(Ch.HipsRoll, -0.08f * w);
                break;
            case Gesture.Stretch:
                // Front paws far ahead, rump up, chest to the floor.
                rig.AddLegs(Ch.Plant0, w);
                rig.Add(Ch.HipsPitch, 0.3f * w);
                rig.Add(Ch.ChestPitch, 0.16f * w);
                rig.AddLeg(Ch.FootZ0, 0, 0.26f * w);
                rig.AddLeg(Ch.FootZ0, 1, 0.26f * w);
                rig.Add(Ch.NeckPitch, -0.3f * w);
                rig.Add(Ch.TailLift, 0.5f * w);
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
                rig.AddLegs(Ch.Plant0, w);
                rig.Add(Ch.HipsY, 0.04f * w);
                rig.Add(Ch.ChestPitch, -0.22f * w);
                rig.Add(Ch.NeckPitch, -0.15f * w);
                rig.Add(Ch.TailLift, 0.5f * w);
                break;
            case Gesture.Droop:
            case Gesture.Sigh:
            case Gesture.Facepalm:
            case Gesture.Shy:
                rig.Add(Ch.NeckPitch, 0.35f * w);
                rig.Add(Ch.HeadPitch, 0.2f * w);
                rig.Add(Ch.TailLift, -0.5f * w);
                break;
        }
    }

    private void BonesMood(ModelAnimalRig rig)
    {
        switch (_emotion)
        {
            case Emotion.Happy:
                rig.Add(Ch.TailLift, 0.35f);
                rig.Add(Ch.TailWag, 0.25f);
                rig.Add(Ch.HeadRoll, 0.07f * MathF.Sin(_time * 2.6f));
                break;
            case Emotion.Sad:
            case Emotion.Worried:
                rig.Add(Ch.NeckPitch, 0.25f);
                rig.Add(Ch.HeadPitch, 0.15f);
                rig.Add(Ch.TailLift, -0.6f);
                break;
            case Emotion.Angry:
                // An arched back and a stiff tail.
                rig.Add(Ch.ChestPitch, -0.06f);
                rig.Add(Ch.NeckPitch, 0.15f);
                rig.Add(Ch.TailLift, 0.45f);
                rig.Add(Ch.TailWag, 0.6f);
                rig.Add(Ch.TailSpeed, 6f);
                break;
            case Emotion.Surprised:
                rig.Add(Ch.NeckPitch, -0.2f);
                rig.Add(Ch.TailLift, 0.4f);
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
