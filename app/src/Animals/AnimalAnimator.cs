using System;
using DigitalDoll.Character;
using DigitalDoll.Core;
using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Mascot;
using Godot;

namespace DigitalDoll.Animals;

/// <summary>
/// Procedural motion for the animals, in the same layered way as the doll: each
/// behaviour adds to a set of pose channels (body lift and tilt, head, legs, tail,
/// ears, eyes, mouth, wings) with a weight that eases in and out, then the channels
/// are applied to the parts. Gestures and moods are the animal versions of the doll's.
/// </summary>
public sealed class AnimalAnimator : IPerformer
{
    private const float WeightRate = 9f;

    private sealed class Pose
    {
        public float RootPitch, RootForward;
        public float BodyY, BodyZ, BodyPitch, BodyRoll, BodyYaw, BodySquash;
        public float HeadPitch, HeadYaw, HeadRoll, HeadY, HeadForward;
        public readonly float[] LegSwing = new float[4], LegSpread = new float[4], LegFold = new float[4];
        public float TailWag, TailLift, TailCurl;
        public readonly float[] Ear = new float[2];
        public float EarDroop;
        public float Open = 1f, Squint, Wide, Angry, Mouth, Tongue, Cheeks;
        public readonly float[] Wing = new float[2];

        public void Clear()
        {
            RootPitch = RootForward = 0f;
            BodyY = BodyZ = BodyPitch = BodyRoll = BodyYaw = BodySquash = 0f;
            HeadPitch = HeadYaw = HeadRoll = HeadY = HeadForward = 0f;
            Array.Clear(LegSwing);
            Array.Clear(LegSpread);
            Array.Clear(LegFold);
            TailWag = TailLift = TailCurl = 0f;
            Array.Clear(Ear);
            EarDroop = 0f;
            Open = 1f;
            Squint = Wide = Angry = Mouth = Tongue = Cheeks = 0f;
            Array.Clear(Wing);
        }
    }

    private sealed class ActiveGesture
    {
        public required GestureCue Cue;
        public float Time;
        public float Fade = 1f;
        public float Phase => Mathf.Clamp(Time / Cue.Duration, 0f, 1f);
        public bool Done => Time >= Cue.Duration || Fade <= 0f;
    }

    private readonly AnimalRig _rig;
    private readonly Random _random;
    private readonly Pose _p = new();
    private readonly PerformanceDirector _fidgetDirector;
    private readonly System.Collections.Generic.Queue<GestureCue> _queue = new();
    private ActiveGesture? _gesture, _fading;

    private float _time, _phase, _climbPhase;
    private float _idle = 1f, _walk, _run, _sit, _lie, _sprawl, _getUp, _tumble, _drag, _fall, _land, _jump, _climb, _dance;
    private bool _dancing;
    private float _slip, _hang, _climbUp;
    private float _pet, _petBody, _talk;
    private float _stateTime;
    private MascotState _state;
    private float _dangle, _dangleVelocity;
    private float _lookYaw, _lookPitch;
    private float _nextBlink = 2f, _blink;
    private float _earTwitch, _nextTwitch = 3f;
    private int _twitchEar;
    private Emotion _emotion = Emotion.Neutral;
    private float _emotionLevel, _emotionTarget, _emotionHold;
    private float _syllable, _syllableLength = 0.12f, _syllableOpen;
    private float _fidgetTimer = 6f;
    private Gesture? _lastFidget;

    public AnimalAnimator(AnimalRig rig, Random random)
    {
        _rig = rig;
        _random = random;
        _fidgetDirector = new PerformanceDirector(random);
    }

    public float LookYaw { get; set; }
    public float LookPitch { get; set; }
    public bool Fidgets { get; set; } = true;
    public bool Talking { get; set; }
    public bool Petted { get; set; }
    public TouchZone PetZone { get; set; }
    public Gesture? CurrentGesture => _gesture?.Cue.Gesture;

    private bool Quadruped => !_rig.Biped;

    public void Play(GestureCue cue, bool now = false)
    {
        if (now || _gesture == null)
        {
            if (_gesture != null)
                _fading = _gesture;
            _gesture = new ActiveGesture { Cue = cue };
            if (now)
                _queue.Clear();
            return;
        }
        if (_queue.Count >= 3)
            _queue.Dequeue();
        _queue.Enqueue(cue);
    }

    public void StopGestures()
    {
        _queue.Clear();
        if (_gesture != null)
        {
            _fading = _gesture;
            _gesture = null;
        }
    }

    /// <summary>Animals have no hands for the doll's poses: a happy little hop instead.</summary>
    public void StrikePose(string id) => Play(new GestureCue(Gesture.Hop, 1.4f, 0.8f, false), now: true);

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

    public void Update(float dt, MascotState state, float speedMetres, float dragVelocityMetres, float climbSpeedMetres = 0f, bool running = false,
        bool bracing = false, ClimbGrip grip = ClimbGrip.Steady)
    {
        // Animals sit on their haunches wherever they are, and dance by bobbing about.
        if (state == MascotState.FloorSit)
            state = MascotState.Sit;
        _dancing = state == MascotState.Dance;
        if (_dancing || state == MascotState.Pose)
            state = MascotState.Idle;
        _time += dt;
        _stateTime = state == _state ? _stateTime + dt : 0f;
        _state = state;
        Ease(ref _idle, state == MascotState.Idle ? 1f : 0f, dt);
        Ease(ref _walk, state == MascotState.Walk ? 1f : 0f, dt);
        Ease(ref _dance, _dancing ? 1f : 0f, dt, 6f);
        Ease(ref _run, running ? 1f : 0f, dt, 6f);
        // Lying down by way of sitting, as a cat does: rump down first, then the front.
        bool settling = state == MascotState.Lie && _stateTime < 0.5f;
        Ease(ref _sit, state == MascotState.Sit || settling ? 1f : 0f, dt, 6f);
        Ease(ref _lie, state == MascotState.Lie && !settling ? 1f : 0f, dt, state == MascotState.Lie ? 3.5f : 5f);
        Ease(ref _sprawl, state == MascotState.Sprawl ? 1f : 0f, dt, state == MascotState.Sprawl ? 16f : 4f);
        Ease(ref _getUp, state == MascotState.GetUp ? 1f : 0f, dt, 6f);
        Ease(ref _tumble, state == MascotState.Tumble ? 1f : 0f, dt, state == MascotState.Tumble ? 14f : 4f);
        Ease(ref _drag, state == MascotState.Dragged ? 1f : 0f, dt);
        Ease(ref _fall, state == MascotState.Falling ? 1f : 0f, dt);
        Ease(ref _land, state == MascotState.Landing ? 1f : 0f, dt, 16f);
        Ease(ref _jump, state == MascotState.Jump ? 1f : 0f, dt, 14f);
        Ease(ref _climb, state is MascotState.Climb or MascotState.Cling ? 1f : 0f, dt);
        Ease(ref _pet, Petted && PetZone == TouchZone.Head ? 1f : 0f, dt, 5f);
        Ease(ref _petBody, Petted && PetZone == TouchZone.Body ? 1f : 0f, dt, 5f);
        Ease(ref _talk, Talking ? 1f : 0f, dt, 10f);

        // Little legs take quick steps: a stride of about one body height.
        float stride = (_rig.Biped ? 0.9f : 1.1f) * _rig.Height * (1f + 0.6f * _run);
        _phase = (_phase + dt * Mathf.Tau * MathF.Abs(speedMetres) / stride) % Mathf.Tau;
        if (grip == ClimbGrip.Steady)
            _climbPhase = (_climbPhase + dt * Mathf.Tau * MathF.Abs(climbSpeedMetres) / (0.6f * _rig.Height)) % Mathf.Tau;
        Ease(ref _slip, state == MascotState.Climb && grip == ClimbGrip.Slipping ? 1f : 0f, dt, 16f);
        Ease(ref _hang, (state is MascotState.Climb or MascotState.Cling) && grip == ClimbGrip.Hanging ? 1f : 0f, dt, 7f);
        Ease(ref _climbUp, state == MascotState.Climb && grip == ClimbGrip.Steady && climbSpeedMetres < 0f ? 1f : 0f, dt, 4f);

        float push = -dragVelocityMetres * 1.5f;
        _dangleVelocity += (push - _dangle * 40f - _dangleVelocity * 5f) * dt;
        _dangle = Mathf.Clamp(_dangle + _dangleVelocity * dt, -0.9f, 0.9f);
        _lookYaw = Mathf.Lerp(_lookYaw, Mathf.Clamp(LookYaw, -0.8f, 0.8f), 1f - MathF.Exp(-6f * dt));
        _lookPitch = Mathf.Lerp(_lookPitch, Mathf.Clamp(LookPitch, -0.5f, 0.5f), 1f - MathF.Exp(-6f * dt));

        AdvanceGestures(dt);
        UpdateFidgets(dt, state);
        UpdateFace(dt);

        _p.Clear();
        PoseRest();
        PoseIdle();
        PoseDance();
        PoseWalk();
        PoseRun();
        PoseSit();
        PoseLie();
        PoseSprawl();
        PoseGetUp();
        PoseTumble();
        PoseDragged();
        PoseFalling();
        PoseLanding();
        PoseJump();
        PoseClimb();
        PosePetting();
        PoseTalking();
        if (_fading != null)
            PoseGesture(_fading);
        if (_gesture != null)
            PoseGesture(_gesture);
        PoseMood();
        PoseLook();
        Apply();
    }

    // ---- Behaviours ---------------------------------------------------------------

    private void PoseRest()
    {
        // Each species carries its tail its own way.
        switch (_rig.Species)
        {
            case Species.Cat:
                _p.TailLift += 0.38f;
                break;
            case Species.Dog:
                _p.TailLift += 0.75f;
                break;
            case Species.Pig:
                _p.TailLift += 0.5f;
                _p.TailCurl += 1.2f;
                break;
        }
    }

    private void PoseIdle()
    {
        float breath = MathF.Sin(_time * Mathf.Tau / 2.8f);
        float w = 0.4f + 0.6f * _idle;
        _p.BodySquash += 0.02f * breath * w;
        w = _idle;
        _p.HeadPitch += 0.04f * MathF.Sin(_time * 0.7f) * w;
        _p.TailWag += (_rig.Species == Species.Dog ? 0.25f * MathF.Sin(_time * 5f) : 0.3f * MathF.Sin(_time * 1.2f)) * w;
        if (_rig.Biped)
        {
            // Hens never keep their head still.
            _p.HeadYaw += 0.25f * MathF.Sin(_time * 0.9f) * MathF.Sin(_time * 2.3f) * w;
            _p.HeadForward += 0.008f * MathF.Max(0f, MathF.Sin(_time * 3.1f)) * w;
        }
    }

    private void PoseDance()
    {
        float w = _dance;
        if (w <= 0.001f)
            return;
        float b = _time * Mathf.Tau;
        _p.BodyY += -0.03f * _rig.LegLength * (0.5f - 0.5f * MathF.Cos(2f * b)) * w;
        _p.HeadYaw += 0.2f * MathF.Sin(b) * w;
        _p.TailWag += 0.8f * MathF.Sin(2f * b) * w;
    }

    private void PoseWalk()
    {
        float w = _walk * (1f - _run);
        if (w <= 0.001f)
            return;
        float φ = _phase;
        if (Quadruped)
        {
            // Trot: diagonal pairs move together.
            for (int i = 0; i < 4; i++)
            {
                float offset = i is 0 or 3 ? 0f : MathF.PI;
                float s = MathF.Sin(φ + offset);
                _p.LegSwing[i] += -0.5f * s * w;
                _p.LegFold[i] += 0.35f * MathF.Max(0f, MathF.Cos(φ + offset)) * w;
            }
            _p.BodyY += 0.005f * MathF.Cos(2f * φ) * w;
            _p.BodyRoll += 0.04f * MathF.Sin(φ) * w;
            _p.HeadPitch += 0.05f * MathF.Sin(2f * φ) * w;
            _p.TailWag += 0.25f * MathF.Sin(φ) * w;
        }
        else
        {
            for (int i = 0; i < 2; i++)
            {
                float s = MathF.Sin(φ + MathF.PI * i);
                _p.LegSwing[i] += -0.6f * s * w;
                _p.LegFold[i] += 0.45f * MathF.Max(0f, MathF.Cos(φ + MathF.PI * i)) * w;
            }
            _p.BodyRoll += 0.08f * MathF.Sin(φ) * w;
            _p.BodyY += 0.006f * MathF.Abs(MathF.Cos(φ)) * w;
            // The head bob: pushed forward, then held while the body catches up.
            float step = (φ / MathF.PI) % 1f;
            _p.HeadForward += (0.025f * step - 0.012f) * w;
        }
    }

    private void PoseRun()
    {
        float w = _run * _walk;
        if (w <= 0.001f)
            return;
        float φ = _phase;
        if (Quadruped)
        {
            // Bounding gallop: front pair, then back pair.
            for (int i = 0; i < 4; i++)
            {
                float offset = (i < 2 ? 0f : MathF.PI) + (i % 2) * 0.4f;
                float s = MathF.Sin(φ + offset);
                _p.LegSwing[i] += -0.85f * s * w;
                _p.LegFold[i] += 0.4f * MathF.Max(0f, MathF.Cos(φ + offset)) * w;
            }
            _p.BodyPitch += 0.14f * MathF.Sin(φ) * w;
            _p.BodyY += 0.014f * MathF.Abs(MathF.Sin(φ)) * w;
            _p.TailLift += -0.3f * w;
        }
        else
        {
            for (int i = 0; i < 2; i++)
                _p.LegSwing[i] += -0.9f * MathF.Sin(φ + MathF.PI * i) * w;
            _p.BodyPitch += 0.25f * w;
            _p.Wing[0] += (0.7f + 0.4f * MathF.Sin(_time * 22f)) * w;
            _p.Wing[1] += (0.7f + 0.4f * MathF.Sin(_time * 22f + 1f)) * w;
        }
        _p.Ear[0] += -0.5f * w;
        _p.Ear[1] += -0.5f * w;
    }

    private void PoseSit()
    {
        float w = _sit;
        if (w <= 0.001f)
            return;
        if (Quadruped)
        {
            // Rump down, front legs straight, head level.
            _p.BodyPitch += -0.55f * w;
            _p.BodyY += -_rig.LegLength * 0.45f * w;
            _p.BodyZ += -0.02f * w;
            _p.LegSwing[0] += 0.55f * w;
            _p.LegSwing[1] += 0.55f * w;
            _p.LegSwing[2] += -1.2f * w;
            _p.LegSwing[3] += -1.2f * w;
            _p.LegFold[2] += 0.55f * w;
            _p.LegFold[3] += 0.55f * w;
            _p.HeadPitch += 0.45f * w;
            _p.TailLift += -0.55f * w;
            _p.TailWag += 0.7f * w;
        }
        else
        {
            // Settled down on its legs like a brooding hen.
            _p.BodyY += -_rig.LegLength * 0.85f * w;
            _p.LegFold[0] += 0.9f * w;
            _p.LegFold[1] += 0.9f * w;
        }
    }

    private void PoseLie()
    {
        float w = _lie;
        if (w <= 0.001f)
            return;
        float breath = MathF.Sin(_time * Mathf.Tau / 4.5f);
        // Curled up: body on the ground, legs tucked in, chin down, tail around.
        _p.BodyY += -(_rig.LegLength - 0.01f) * w;
        for (int i = 0; i < 4; i++)
            _p.LegFold[i] += 0.85f * w;
        if (Quadruped)
        {
            _p.LegSwing[0] += -0.3f * w;
            _p.LegSwing[1] += -0.3f * w;
            _p.LegSwing[2] += 0.3f * w;
            _p.LegSwing[3] += 0.3f * w;
        }
        _p.HeadPitch += 0.35f * w;
        _p.HeadY += -0.015f * w;
        _p.TailWag += 0.9f * w;
        _p.TailLift += -0.45f * w;
        _p.BodySquash += 0.025f * breath * w;
        _p.Open -= 1f * w;
        _p.Ear[0] += -0.2f * w;
        _p.Ear[1] += -0.2f * w;
    }

    private void PoseSprawl()
    {
        float w = _sprawl;
        if (w <= 0.001f)
            return;
        float t = _stateTime;
        float bounce = MathF.Exp(-t * 8f) * MathF.Sin(t * 26f);
        // Splat: belly on the ground, legs flat out front and back.
        _p.BodyY += -(_rig.LegLength * 0.92f - 0.01f * bounce) * w;
        if (Quadruped)
        {
            _p.LegSwing[0] += -1.35f * w;
            _p.LegSwing[1] += -1.35f * w;
            _p.LegSwing[2] += 1.35f * w;
            _p.LegSwing[3] += 1.35f * w;
            for (int i = 0; i < 4; i++)
                _p.LegSpread[i] += 0.3f * w;
        }
        else
        {
            _p.LegSwing[0] += 1.3f * w;
            _p.LegSwing[1] += 1.3f * w;
            _p.Wing[0] += 1.2f * w;
            _p.Wing[1] += 1.2f * w;
        }
        _p.HeadPitch += 0.25f * w;
        _p.HeadY += -0.01f * w;
        _p.EarDroop += 0.6f * w;
        _p.TailLift += -0.6f * w;
        _p.Open -= 0.6f * w;
        // A feeble kick after a moment.
        float kick = MathF.Max(0f, MathF.Sin(t * 8f)) * Smooth(Mathf.Clamp((t - 0.8f) / 0.4f, 0f, 1f)) * MathF.Exp(-MathF.Max(0f, t - 1.4f) * 2f);
        _p.LegSwing[_rig.Legs.Length - 1] += -0.5f * kick * w;
    }

    private void PoseGetUp()
    {
        float w = _getUp;
        if (w <= 0.001f)
            return;
        // Half up, then a full-body shake-off.
        _p.BodyY += -_rig.LegLength * 0.3f * w;
        for (int i = 0; i < 4; i++)
            _p.LegFold[i] += 0.3f * w;
        float shake = MathF.Sin(_time * 32f) * Smooth(Mathf.Clamp((_stateTime - 0.3f) * 4f, 0f, 1f));
        _p.BodyRoll += 0.14f * shake * w;
        _p.HeadRoll += -0.25f * shake * w;
        _p.Open -= 0.4f * w;
    }

    private void PoseTumble()
    {
        float w = _tumble;
        if (w <= 0.001f)
            return;
        // Flipped onto its back, legs wiggling in the air, then rolls back over.
        _p.BodyRoll += MathF.PI * w;
        _p.BodyY += -(_rig.BodyHeight - _rig.BodyRadiusY) * w;
        for (int i = 0; i < _rig.Legs.Length; i++)
            _p.LegSwing[i] += 0.45f * MathF.Sin(_time * 13f + i * 1.7f) * w;
        if (!Quadruped)
        {
            _p.Wing[0] += (0.6f + 0.5f * MathF.Sin(_time * 20f)) * w;
            _p.Wing[1] += (0.6f + 0.5f * MathF.Sin(_time * 20f + 1f)) * w;
        }
        _p.Open -= 0.5f * w;
        _p.Mouth += 0.4f * w;
    }

    private void PoseDragged()
    {
        float w = _drag;
        if (w <= 0.001f)
            return;
        // Held up by the scruff: nose up, legs hanging and paddling.
        float tilt = Quadruped ? 1.1f : 0.4f;
        _p.BodyPitch += -tilt * w;
        for (int i = 0; i < _rig.Legs.Length; i++)
            _p.LegSwing[i] += (tilt + _dangle + 0.25f * MathF.Sin(_time * 9f + i * 1.9f)) * w;
        _p.TailLift += -1.0f * w;
        _p.Ear[0] += -0.3f * w;
        _p.Ear[1] += -0.3f * w;
        _p.Wide += 0.5f * w;
        _p.Mouth += 0.3f * w;
        if (!Quadruped)
        {
            _p.Wing[0] += (1.0f + 0.6f * MathF.Sin(_time * 26f)) * w;
            _p.Wing[1] += (1.0f + 0.6f * MathF.Sin(_time * 26f + 0.8f)) * w;
        }
    }

    private void PoseFalling()
    {
        float w = _fall;
        if (w <= 0.001f)
            return;
        // Splayed out like a flying squirrel.
        for (int i = 0; i < 4; i++)
            _p.LegSpread[i] += 0.65f * w;
        if (Quadruped)
        {
            _p.LegSwing[0] += -0.5f * w;
            _p.LegSwing[1] += -0.5f * w;
            _p.LegSwing[2] += 0.5f * w;
            _p.LegSwing[3] += 0.5f * w;
        }
        else
        {
            _p.Wing[0] += (1.1f + 0.6f * MathF.Sin(_time * 28f)) * w;
            _p.Wing[1] += (1.1f + 0.6f * MathF.Sin(_time * 28f + 0.8f)) * w;
        }
        _p.Ear[0] += -0.6f * w;
        _p.Ear[1] += -0.6f * w;
        _p.TailLift += 0.6f * w;
        _p.Wide += 0.8f * w;
        _p.Mouth += 0.4f * w;
    }

    private void PoseLanding()
    {
        float w = _land;
        if (w <= 0.001f)
            return;
        _p.BodySquash += -0.13f * w;
        _p.BodyY += -0.015f * w;
        for (int i = 0; i < 4; i++)
            _p.LegFold[i] += 0.4f * w;
    }

    private void PoseJump()
    {
        float w = _jump;
        if (w <= 0.001f)
            return;
        if (Quadruped)
        {
            _p.LegSwing[0] += -0.9f * w;
            _p.LegSwing[1] += -0.9f * w;
            _p.LegSwing[2] += 0.9f * w;
            _p.LegSwing[3] += 0.9f * w;
        }
        else
        {
            _p.LegSwing[0] += 0.6f * w;
            _p.LegSwing[1] += 0.6f * w;
            _p.Wing[0] += (1.2f + 0.5f * MathF.Sin(_time * 24f)) * w;
            _p.Wing[1] += (1.2f + 0.5f * MathF.Sin(_time * 24f + 0.8f)) * w;
        }
        _p.BodySquash += 0.07f * w;
        _p.Ear[0] += -0.3f * w;
        _p.Ear[1] += -0.3f * w;
    }

    private void PoseClimb()
    {
        float w = _climb;
        if (w <= 0.001f)
            return;
        // Belly to the wall: the whole animal turns nose-up, paws on the wall.
        _p.RootPitch += -MathF.PI / 2f * w;
        _p.RootForward += (_rig.BodyHeight - _rig.LegLength * 0.9f) * w;
        float s = MathF.Sin(_climbPhase);
        float steady = w * (1f - MathF.Max(_slip, _hang));
        float heave = MathF.Max(0f, s) * _climbUp;
        if (Quadruped)
        {
            // Front paws reach up in turns with big strokes; the back legs push off
            // together, so the body bunches and stretches like a cat going up a tree.
            _p.LegSwing[0] += (-1.25f - 0.6f * s) * steady;
            _p.LegSwing[1] += (-1.25f + 0.6f * s) * steady;
            float push = MathF.Sin(_climbPhase * 2f);
            _p.LegSwing[2] += (-0.3f + 0.45f * push) * steady;
            _p.LegSwing[3] += (-0.3f + 0.45f * push) * steady;
            _p.LegFold[2] += 0.25f * MathF.Max(0f, -push) * steady;
            _p.LegFold[3] += 0.25f * MathF.Max(0f, -push) * steady;
            for (int i = 0; i < 4; i++)
                _p.LegSpread[i] += 0.3f * steady;
            _p.BodyPitch += 0.12f * push * steady;
            _p.BodyRoll += 0.12f * s * steady;
            _p.BodyY += 0.01f * push * steady;
        }
        else
        {
            _p.LegSwing[0] += (-0.4f - 0.5f * s) * steady;
            _p.LegSwing[1] += (-0.4f + 0.5f * s) * steady;
            _p.Wing[0] += (0.8f + 0.5f * MathF.Sin(_time * 18f)) * steady;
            _p.Wing[1] += (0.8f + 0.5f * MathF.Sin(_time * 18f + 1f)) * steady;
        }
        // Looking up at where it is going; the tail hangs and sways for balance.
        _p.HeadPitch += -(0.4f + 0.1f * heave) * steady;
        _p.HeadRoll += 0.1f * s * _climbUp * steady;
        _p.TailLift += -0.7f * w;
        _p.TailWag += 0.5f * MathF.Sin(_time * 2.4f) * steady;
        _p.Squint += 0.35f * heave;

        // Slipping: every paw scrabbling at the wall, ears back, eyes wide, sliding.
        if (_slip > 0.001f)
        {
            float sl = w * _slip;
            float scrabble = MathF.Sin(_time * 26f);
            if (Quadruped)
            {
                _p.LegSwing[0] += (-1.9f + 0.35f * scrabble) * sl;
                _p.LegSwing[1] += (-1.9f - 0.35f * scrabble) * sl;
                _p.LegSwing[2] += (-0.4f + 0.6f * scrabble) * sl;
                _p.LegSwing[3] += (-0.4f - 0.6f * scrabble) * sl;
                for (int i = 0; i < 4; i++)
                    _p.LegSpread[i] += 0.45f * sl;
            }
            else
            {
                _p.LegSwing[0] += (-0.4f + 0.7f * scrabble) * sl;
                _p.LegSwing[1] += (-0.4f - 0.7f * scrabble) * sl;
                _p.Wing[0] += (1.6f + 0.6f * MathF.Sin(_time * 30f)) * sl;
                _p.Wing[1] += (1.6f + 0.6f * MathF.Sin(_time * 30f + 0.8f)) * sl;
            }
            _p.RootPitch += 0.15f * sl;
            _p.HeadPitch += 0.2f * sl;
            _p.Ear[0] += -0.7f * sl;
            _p.Ear[1] += -0.7f * sl;
            _p.Wide += 1f * sl;
            _p.Mouth += 0.5f * sl;
            _p.TailWag += 0.8f * MathF.Sin(_time * 14f) * sl;
        }

        // Caught again: hanging by the front paws, back legs dangling, then a heave.
        if (_hang > 0.001f)
        {
            float hg = w * _hang;
            float swing = MathF.Sin(_time * 3.4f);
            if (Quadruped)
            {
                _p.LegSwing[0] += -2.0f * hg;
                _p.LegSwing[1] += -1.85f * hg;
                _p.LegSwing[2] += (0.35f + 0.25f * swing) * hg;
                _p.LegSwing[3] += (0.35f - 0.25f * swing) * hg;
                _p.LegSpread[0] += 0.3f * hg;
                _p.LegSpread[1] += 0.3f * hg;
            }
            else
            {
                _p.LegSwing[0] += (0.3f + 0.2f * swing) * hg;
                _p.LegSwing[1] += (0.3f - 0.2f * swing) * hg;
                _p.Wing[0] += 1.2f * hg;
                _p.Wing[1] += 1.2f * hg;
            }
            _p.BodyRoll += 0.1f * swing * hg;
            _p.HeadPitch += -0.5f * hg;
            _p.EarDroop += 0.4f * hg;
            _p.Squint += 0.3f * hg;
            _p.TailWag += 0.4f * swing * hg;
        }
    }

    private void PosePetting()
    {
        if (_pet > 0.001f)
        {
            // Pushing into the hand with eyes closed; cats raise the tail, dogs wag.
            float sway = MathF.Sin(_time * 3.1f);
            _p.HeadPitch += -0.15f * _pet;
            _p.HeadRoll += 0.22f * sway * _pet;
            _p.Squint += 1f * _pet;
            _p.Ear[0] += -0.35f * _pet;
            _p.Ear[1] += -0.35f * _pet;
            _p.Cheeks += 0.6f * _pet;
            if (_rig.Species == Species.Dog)
                _p.TailWag += 0.6f * MathF.Sin(_time * 16f) * _pet;
            else
                _p.TailLift += 0.4f * _pet;
            _p.BodyY += 0.0008f * MathF.Sin(_time * 70f) * _pet;
        }
        if (_petBody > 0.001f)
        {
            float wriggle = MathF.Sin(_time * 10f);
            _p.BodyRoll += 0.15f * wriggle * _petBody;
            _p.HeadYaw += 0.3f * _petBody;
            _p.Squint += 0.6f * _petBody;
            _p.Cheeks += 1f * _petBody;
            _p.Mouth += 0.3f * _petBody;
            _p.TailWag += 0.5f * wriggle * _petBody;
        }
    }

    private void PoseTalking()
    {
        if (_talk <= 0.001f)
            return;
        _p.Mouth += 0.75f * _syllableOpen * _talk;
        _p.HeadPitch += -0.06f * _syllableOpen * _talk;
        _p.HeadRoll += 0.05f * MathF.Sin(_time * 1.7f) * _talk;
    }

    private void PoseMood()
    {
        float level = _emotionLevel;
        if (level > 0.01f)
        {
            switch (_emotion)
            {
                case Emotion.Happy:
                    _p.Squint += 0.65f * level;
                    _p.Cheeks += 0.6f * level;
                    _p.Ear[0] += 0.2f * level;
                    _p.Ear[1] += 0.2f * level;
                    _p.Mouth += 0.15f * level;
                    if (_rig.Species == Species.Dog)
                    {
                        _p.TailWag += 0.7f * MathF.Sin(_time * 15f) * level;
                        _p.Tongue += level;
                    }
                    break;
                case Emotion.Angry:
                    _p.Angry += level;
                    _p.Ear[0] += -0.8f * level;
                    _p.Ear[1] += -0.8f * level;
                    if (_rig.Species == Species.Cat)
                        _p.TailWag += 0.6f * MathF.Sin(_time * 6f) * level;
                    break;
                case Emotion.Sad:
                case Emotion.Worried:
                    _p.EarDroop += 0.7f * level;
                    _p.HeadPitch += 0.15f * level;
                    _p.TailLift += -0.5f * level;
                    _p.Open -= 0.15f * level;
                    break;
                case Emotion.Relaxed:
                    _p.Squint += 0.5f * level;
                    break;
                case Emotion.Surprised:
                    _p.Wide += level;
                    _p.Ear[0] += 0.4f * level;
                    _p.Ear[1] += 0.4f * level;
                    _p.Mouth += 0.35f * level;
                    break;
                case Emotion.Shy:
                    _p.Squint += 0.4f * level;
                    _p.Cheeks += 1f * level;
                    _p.HeadPitch += 0.15f * level;
                    _p.HeadYaw += 0.25f * level;
                    break;
            }
        }
        if (_earTwitch > 0f)
            _p.Ear[_twitchEar] += -0.6f * MathF.Sin(_earTwitch / 0.25f * MathF.PI);
        _p.Open -= MathF.Sin(_blink / 0.16f * MathF.PI);
    }

    private void PoseLook()
    {
        float w = 1f - MathF.Max(MathF.Max(_lie, _sprawl), MathF.Max(_tumble, _drag));
        _p.HeadYaw += 0.8f * _lookYaw * w;
        _p.HeadPitch += -0.7f * _lookPitch * w;
    }

    // ---- Gestures -----------------------------------------------------------------

    private float Allowance(GestureReach reach)
    {
        float head = 1f - MathF.Max(MathF.Max(_drag, _fall), MathF.Max(MathF.Max(_jump, _tumble), MathF.Max(_sprawl, _getUp)));
        float paws = head * (1f - MathF.Max(MathF.Max(_climb, _lie), _walk));
        return reach switch
        {
            GestureReach.Head => head * (1f - 0.5f * _lie),
            GestureReach.Upper => paws,
            GestureReach.Whole => paws * (1f - _sit),
            GestureReach.Seated => _sit * head,
            _ => 0f,
        };
    }

    private void AdvanceGestures(float dt)
    {
        if (_fading != null)
        {
            _fading.Time += dt;
            _fading.Fade -= dt / 0.25f;
            if (_fading.Done)
                _fading = null;
        }
        if (_gesture != null)
        {
            _gesture.Time += dt;
            if (_gesture.Done)
                _gesture = null;
        }
        if (_gesture == null && _queue.Count > 0)
            _gesture = new ActiveGesture { Cue = _queue.Dequeue() };
    }

    /// <summary>Lifts a front paw (or a wing): <paramref name="side"/> +1 left, -1 right.</summary>
    private void Paw(int side, float lift, float wave, float w)
    {
        int index = side > 0 ? 0 : 1;
        if (Quadruped)
        {
            _p.LegSwing[index] += -(1.5f * lift) * w;
            _p.LegFold[index] += 0.25f * lift * w;
            _p.LegSpread[index] += 0.35f * wave * w;
            _p.BodyPitch += -0.12f * lift * w;
            _p.BodyRoll += -side * 0.05f * lift * w;
        }
        else
        {
            _p.Wing[index] += (1.4f * lift + 0.5f * wave) * w;
        }
    }

    private void Hop(float lift, float w)
    {
        _p.BodyY += 0.045f * lift * w;
        for (int i = 0; i < 4; i++)
            _p.LegFold[i] += 0.3f * (1f - lift) * w;
    }

    private void PoseGesture(ActiveGesture active)
    {
        var cue = active.Cue;
        float t = active.Time, d = cue.Duration;
        float envelope = Smooth(Mathf.Clamp(t / 0.25f, 0f, 1f)) * Smooth(Mathf.Clamp((d - t) / 0.35f, 0f, 1f));
        float w = envelope * Mathf.Clamp(active.Fade, 0f, 1f) * Allowance(GestureInfo.Reach(cue.Gesture));
        if (w <= 0.001f)
            return;
        float a = cue.Amplitude, u = active.Phase;
        int p = cue.Mirror ? 1 : -1;
        bool canHop = Allowance(GestureReach.Whole) > 0.5f;

        switch (cue.Gesture)
        {
            case Gesture.Nod:
                _p.HeadPitch += 0.3f * a * (0.5f - 0.5f * MathF.Cos(u * Mathf.Tau * 2f)) * w;
                break;
            case Gesture.Shake:
                _p.HeadYaw += 0.45f * a * MathF.Sin(u * Mathf.Tau * 2.5f) * w;
                break;
            case Gesture.Tilt:
            case Gesture.Shrug:
                // The classic puppy head tilt.
                _p.HeadRoll += p * 0.4f * a * w;
                _p.Ear[p > 0 ? 0 : 1] += 0.3f * w;
                _p.Ear[p > 0 ? 1 : 0] += -0.2f * w;
                break;
            case Gesture.Wave:
                Paw(p, 1f, MathF.Sin(t * 12f), w);
                break;
            case Gesture.Point:
            case Gesture.ThumbsUp:
            case Gesture.Idea:
                Paw(p, 0.9f, 0f, w);
                _p.HeadPitch += -0.12f * w;
                break;
            case Gesture.Count:
            {
                float beat = (u * (Math.Max(1, cue.Count) + 0.5f)) % 1f;
                Paw(p, 0.6f + 0.3f * MathF.Exp(-beat * 6f) * MathF.Sin(beat * MathF.PI * 2f), 0f, w);
                break;
            }
            case Gesture.Explain:
                Paw(p, 0.5f + 0.3f * MathF.Max(0f, MathF.Sin(u * Mathf.Tau * 1.5f)), 0f, w);
                _p.HeadPitch += 0.06f * MathF.Sin(u * Mathf.Tau * 3f) * w;
                break;
            case Gesture.Bow:
            {
                // The play bow: front down, rear up, tail going.
                float bow = MathF.Pow(MathF.Sin(MathF.PI * u), 0.6f) * a;
                _p.BodyPitch += 0.4f * bow * w;
                _p.BodyY += -0.015f * bow * w;
                if (Quadruped)
                {
                    _p.LegSwing[0] += -0.6f * bow * w;
                    _p.LegSwing[1] += -0.6f * bow * w;
                    _p.LegFold[0] += 0.4f * bow * w;
                    _p.LegFold[1] += 0.4f * bow * w;
                }
                _p.TailWag += 0.6f * MathF.Sin(t * 14f) * w;
                break;
            }
            case Gesture.Cheer:
            case Gesture.Hop:
            case Gesture.Clap:
            case Gesture.FistPump:
            {
                float lift = MathF.Max(0f, MathF.Sin(u * 2f % 1f * Mathf.Tau));
                if (canHop)
                    Hop(lift * a, w);
                _p.TailWag += 0.6f * MathF.Sin(t * 16f) * w;
                _p.Wing[0] += (0.6f + 0.6f * lift) * w;
                _p.Wing[1] += (0.6f + 0.6f * lift) * w;
                _p.Squint += 0.5f * w;
                break;
            }
            case Gesture.Think:
                _p.HeadRoll += p * 0.25f * w;
                _p.HeadPitch += -0.2f * w;
                _p.HeadYaw += p * 0.2f * w;
                break;
            case Gesture.Facepalm:
                Paw(p, 1.15f, 0f, w);
                _p.HeadPitch += 0.3f * w;
                _p.Open -= 0.8f * w;
                break;
            case Gesture.Shy:
                _p.HeadPitch += 0.25f * w;
                _p.HeadYaw += p * 0.35f * w;
                _p.Squint += 0.5f * w;
                _p.Cheeks += 1f * w;
                _p.BodyRoll += 0.06f * MathF.Sin(t * 3.2f) * w;
                break;
            case Gesture.Pout:
                _p.HeadYaw += p * 0.6f * a * w;
                _p.HeadPitch += -0.15f * w;
                _p.Angry += 0.8f * w;
                _p.Ear[0] += -0.6f * w;
                _p.Ear[1] += -0.6f * w;
                _p.TailWag += 0.6f * MathF.Sin(t * 7f) * w;
                break;
            case Gesture.Startle:
            {
                float jolt = MathF.Exp(-t * 5f) * MathF.Min(1f, t * 20f);
                if (canHop)
                    _p.BodyY += 0.04f * jolt * w;
                // Fur on end.
                _p.BodySquash += 0.12f * jolt * w;
                _p.Ear[0] += 0.4f * jolt * w;
                _p.Ear[1] += 0.4f * jolt * w;
                _p.TailLift += 0.8f * jolt * w;
                _p.Wide += 1f * w;
                break;
            }
            case Gesture.Giggle:
                _p.BodyRoll += 0.06f * MathF.Sin(t * 20f) * w;
                _p.Squint += 0.7f * w;
                _p.Mouth += 0.4f * w;
                break;
            case Gesture.Droop:
                _p.HeadPitch += 0.4f * w;
                _p.EarDroop += 0.6f * w;
                _p.TailLift += -0.6f * w;
                break;
            case Gesture.Sigh:
            {
                float s = MathF.Sin(MathF.PI * u);
                _p.BodySquash += 0.06f * s * w;
                _p.HeadPitch += (0.2f * u - 0.1f * s) * w;
                break;
            }
            case Gesture.Stretch:
            {
                float s = MathF.Sin(MathF.PI * u);
                if (Quadruped)
                {
                    // Front paws forward, rear in the air.
                    _p.LegSwing[0] += -1.2f * s * w;
                    _p.LegSwing[1] += -1.2f * s * w;
                    _p.BodyPitch += 0.35f * s * w;
                    _p.BodyY += -0.02f * s * w;
                    _p.TailLift += 0.5f * s * w;
                }
                else
                {
                    _p.Wing[0] += 1.4f * s * w;
                    _p.Wing[1] += 1.4f * s * w;
                    _p.HeadPitch += -0.3f * s * w;
                }
                _p.Mouth += 0.7f * s * w;
                _p.Open -= 0.8f * s * w;
                break;
            }
            case Gesture.LookAround:
                _p.HeadYaw += 0.7f * MathF.Sin(u * Mathf.Tau) * a * w;
                _p.HeadPitch += -0.12f * MathF.Sin(u * Mathf.Tau * 2f) * w;
                break;
            case Gesture.FixHair:
                // Grooming: a paw to the mouth and licking (preening under a wing).
                if (Quadruped)
                {
                    Paw(p, 0.85f, 0f, w);
                    _p.HeadPitch += 0.35f * w;
                    _p.HeadYaw += p * 0.3f * w;
                    _p.Mouth += 0.3f * MathF.Max(0f, MathF.Sin(t * 9f)) * w;
                }
                else
                {
                    _p.HeadYaw += p * 1.0f * w;
                    _p.HeadPitch += 0.4f * w;
                    _p.Wing[p > 0 ? 0 : 1] += 0.5f * w;
                }
                _p.Squint += 0.7f * w;
                break;
            case Gesture.Yawn:
            {
                float s = MathF.Sin(MathF.PI * Mathf.Clamp(u * 1.25f, 0f, 1f));
                _p.Mouth += 1f * s * w;
                _p.HeadPitch += -0.3f * s * w;
                _p.Open -= 1f * s * w;
                _p.Ear[0] += -0.3f * s * w;
                _p.Ear[1] += -0.3f * s * w;
                break;
            }
            case Gesture.ShiftWeight:
            {
                // Scratching an ear with a back paw (or a little wing shuffle).
                float s = MathF.Sin(MathF.PI * u);
                if (Quadruped)
                {
                    int back = p > 0 ? 2 : 3;
                    _p.LegSwing[back] += (-1.1f + 0.25f * MathF.Sin(t * 26f)) * s * w;
                    _p.LegSpread[back] += 0.3f * s * w;
                    _p.HeadRoll += -p * 0.35f * s * w;
                    _p.BodyRoll += p * 0.08f * s * w;
                }
                else
                {
                    _p.Wing[0] += 0.4f * s * MathF.Abs(MathF.Sin(t * 12f)) * w;
                    _p.Wing[1] += 0.4f * s * MathF.Abs(MathF.Sin(t * 12f)) * w;
                }
                _p.Squint += 0.6f * s * w;
                break;
            }
            case Gesture.KickLegs:
                _p.TailWag += 0.7f * MathF.Sin(t * 4f) * w;
                _p.HeadYaw += 0.15f * MathF.Sin(t * 1.5f) * w;
                break;
        }
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
            MascotState.Sit => new[] { Gesture.FixHair, Gesture.LookAround, Gesture.Yawn, Gesture.KickLegs, Gesture.ShiftWeight },
            MascotState.Cling => new[] { Gesture.LookAround },
            _ => new[] { Gesture.Stretch, Gesture.LookAround, Gesture.FixHair, Gesture.Yawn, Gesture.ShiftWeight, Gesture.Tilt },
        };
        var pick = options[_random.Next(options.Length)];
        if (pick == _lastFidget && options.Length > 1)
            pick = options[(Array.IndexOf(options, pick) + 1) % options.Length];
        _lastFidget = pick;
        Play(_fidgetDirector.Vary(pick, 0.4f + 0.4f * (float)_random.NextDouble()));
    }

    private void UpdateFace(float dt)
    {
        _nextBlink -= dt;
        if (_nextBlink <= 0f)
        {
            _blink = 0.16f;
            _nextBlink = 2f + (float)_random.NextDouble() * 4f;
        }
        _blink = MathF.Max(0f, _blink - dt);

        _nextTwitch -= dt;
        if (_nextTwitch <= 0f)
        {
            _earTwitch = 0.25f;
            _twitchEar = _random.Next(2);
            _nextTwitch = 2f + (float)_random.NextDouble() * 6f;
        }
        _earTwitch = MathF.Max(0f, _earTwitch - dt);

        _emotionHold -= dt;
        if (_emotionHold <= 0f && !Talking)
            _emotionTarget = 0f;
        _emotionLevel = Mathf.Lerp(_emotionLevel, _emotionTarget, 1f - MathF.Exp(-(_emotionTarget > _emotionLevel ? 8f : 2f) * dt));

        if (_talk > 0.01f)
        {
            _syllable += dt;
            if (_syllable >= _syllableLength)
            {
                _syllable = 0f;
                _syllableLength = 0.1f + 0.1f * (float)_random.NextDouble();
            }
            _syllableOpen = MathF.Sin(MathF.PI * _syllable / _syllableLength);
        }
        else
        {
            _syllableOpen = 0f;
        }
    }

    // ---- Applying the pose ----------------------------------------------------------

    private void Apply()
    {
        var rest = _rig.Rest;
        var p = _p;

        _rig.Root.Transform = new Transform3D(new Basis(Vector3.Right, p.RootPitch), new Vector3(0f, 0f, p.RootForward)) * rest[_rig.Root];

        var bodyRest = rest[_rig.Body];
        var bodyBasis = Basis.FromEuler(new Vector3(p.BodyPitch, p.BodyYaw, p.BodyRoll), EulerOrder.Yxz)
            .Scaled(new Vector3(1f - 0.4f * p.BodySquash, 1f + p.BodySquash, 1f - 0.4f * p.BodySquash));
        _rig.Body.Transform = new Transform3D(bodyRest.Basis * bodyBasis, bodyRest.Origin + new Vector3(0f, p.BodyY, p.BodyZ));

        var headRest = rest[_rig.Head];
        _rig.Head.Transform = new Transform3D(
            headRest.Basis * Basis.FromEuler(new Vector3(p.HeadPitch, p.HeadYaw, p.HeadRoll), EulerOrder.Yxz),
            headRest.Origin + new Vector3(0f, p.HeadY, p.HeadForward));

        for (int i = 0; i < _rig.Legs.Length; i++)
        {
            float side = i % 2 == 0 ? 1f : -1f;
            var legRest = rest[_rig.Legs[i]];
            var basis = Basis.FromEuler(new Vector3(p.LegSwing[i], 0f, p.LegSpread[i] * side), EulerOrder.Zxy)
                .Scaled(new Vector3(1f, 1f - 0.65f * Mathf.Clamp(p.LegFold[i], 0f, 1f), 1f));
            _rig.Legs[i].Transform = new Transform3D(legRest.Basis * basis, legRest.Origin);
        }

        for (int i = 0; i < _rig.Tail.Length; i++)
        {
            var segment = _rig.Tail[i];
            float k = 1f / _rig.Tail.Length;
            var basis = Basis.FromEuler(new Vector3(p.TailLift * k * 1.6f, p.TailWag * k * 1.8f, p.TailCurl * k), EulerOrder.Yxz);
            segment.Transform = new Transform3D(rest[segment].Basis * basis, rest[segment].Origin);
        }

        for (int i = 0; i < _rig.Ears.Length; i++)
        {
            float side = i == 0 ? 1f : -1f;
            var basis = Basis.FromEuler(new Vector3(p.Ear[i] * 0.6f, 0f, side * p.EarDroop * 0.8f), EulerOrder.Xyz);
            _rig.Ears[i].Transform = new Transform3D(rest[_rig.Ears[i]].Basis * basis, rest[_rig.Ears[i]].Origin);
        }

        for (int i = 0; i < _rig.Wings.Length; i++)
        {
            float side = i == 0 ? 1f : -1f;
            var basis = new Basis(Vector3.Back, side * Mathf.Clamp(p.Wing[i], -0.2f, 2.2f));
            _rig.Wings[i].Transform = new Transform3D(rest[_rig.Wings[i]].Basis * basis, rest[_rig.Wings[i]].Origin);
        }

        // Eyes: blinks and squints squash them; wide eyes grow; cross eyes slant.
        float open = Mathf.Clamp(p.Open, 0.08f, 1f) * (1f - 0.65f * Mathf.Clamp(p.Squint, 0f, 1f));
        float wide = 1f + 0.3f * Mathf.Clamp(p.Wide, 0f, 1f);
        for (int i = 0; i < _rig.Eyes.Length; i++)
        {
            float side = i == 0 ? 1f : -1f;
            var basis = new Basis(Vector3.Back, -side * 0.45f * Mathf.Clamp(p.Angry, 0f, 1f))
                .Scaled(new Vector3(wide, Mathf.Max(open * wide, 0.08f), 1f));
            _rig.Eyes[i].Transform = new Transform3D(rest[_rig.Eyes[i]].Basis * basis, rest[_rig.Eyes[i]].Origin);
        }
        foreach (var cheek in _rig.Cheeks)
            cheek.Scale = Vector3.One * (1f + 0.6f * Mathf.Clamp(p.Cheeks, 0f, 1.5f));

        float mouth = Mathf.Clamp(p.Mouth, 0f, 1f);
        if (_rig.Biped)
            _rig.Mouth.Transform = new Transform3D(new Basis(Vector3.Right, 0.6f * mouth), rest[_rig.Mouth].Origin);
        else
            _rig.Mouth.Scale = new Vector3(1f, 0.05f + 0.95f * mouth, 1f);
        if (_rig.Tongue != null)
            _rig.Tongue.Visible = Mathf.Clamp(p.Tongue, 0f, 1f) * mouth > 0.05f || p.Tongue > 0.5f;
        if (_rig.Tongue != null && p.Tongue > 0.5f && mouth < 0.3f)
            _rig.Mouth.Scale = new Vector3(1f, 0.45f, 1f);
    }

    private static void Ease(ref float value, float target, float dt, float rate = WeightRate) =>
        value = Mathf.Lerp(value, target, 1f - MathF.Exp(-rate * dt));

    private static float Smooth(float x) => x * x * (3f - 2f * x);
}
