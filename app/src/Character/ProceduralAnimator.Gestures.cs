using System;
using DigitalDoll.Core.Acting;
using Godot;

namespace DigitalDoll.Character;

/// <summary>
/// Gestures: short motions layered on top of the current behaviour. Each is a
/// function of its phase (0 → 1) and envelope weight, with the chosen variation
/// (size, side) from <see cref="GestureCue"/>.
/// </summary>
public sealed partial class ProceduralAnimator
{
    private sealed class ActiveGesture
    {
        public required GestureCue Cue;
        public float Time;
        public float Fade = 1f;
        public bool FadingOut;

        public float Phase => Mathf.Clamp(Time / Cue.Duration, 0f, 1f);
        public bool Done => Time >= Cue.Duration || Fade <= 0f;
    }

    private const int MaxQueued = 3;

    private ActiveGesture? _gesture, _fading;
    private readonly System.Collections.Generic.Queue<GestureCue> _queue = new();

    /// <summary>The gesture playing now, if any.</summary>
    public Gesture? CurrentGesture => _gesture?.Cue.Gesture;

    /// <summary>Queues a gesture; <paramref name="now"/> cross-fades into it straight away.</summary>
    public void Play(GestureCue cue, bool now = false)
    {
        if (now || _gesture == null)
        {
            if (_gesture != null)
            {
                _fading = _gesture;
                _fading.FadingOut = true;
            }
            _gesture = new ActiveGesture { Cue = cue };
            if (now)
                _queue.Clear();
            return;
        }
        if (_queue.Count >= MaxQueued)
            _queue.Dequeue();
        _queue.Enqueue(cue);
    }

    /// <summary>Drops every queued and playing gesture (they fade out).</summary>
    public void StopGestures()
    {
        _queue.Clear();
        if (_gesture != null)
        {
            _fading = _gesture;
            _fading.FadingOut = true;
            _gesture = null;
        }
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

    /// <summary>How much of a gesture the current behaviour allows (0 while falling, climbing…).</summary>
    private float Allowance(GestureReach reach)
    {
        float head = 1f - MathF.Max(MathF.Max(_drag, _fall), MathF.Max(MathF.Max(_jump, _tumble), MathF.Max(_sprawl, _getUp)));
        float upper = head * (1f - MathF.Max(MathF.Max(_climb, _lie), MathF.Max(_dance, _pose)));
        return reach switch
        {
            GestureReach.Head => head * (1f - 0.5f * _lie),
            GestureReach.Upper => upper,
            GestureReach.Whole => upper * (1f - MathF.Max(MathF.Max(_walk, _sit), _floorSit)),
            GestureReach.Seated => _sit * head,
            _ => 0f,
        };
    }

    private void PoseGestures()
    {
        if (_fading != null)
            PoseGesture(_fading);
        if (_gesture != null)
            PoseGesture(_gesture);
    }

    private void PoseGesture(ActiveGesture active)
    {
        var cue = active.Cue;
        float t = active.Time, d = cue.Duration;
        float envelope = Smooth(Mathf.Clamp(t / 0.25f, 0f, 1f)) * Smooth(Mathf.Clamp((d - t) / 0.35f, 0f, 1f));
        float w = envelope * Mathf.Clamp(active.Fade, 0f, 1f) * Allowance(GestureInfo.Reach(cue.Gesture));
        if (w <= 0.001f)
            return;
        float a = cue.Amplitude;
        float u = active.Phase;
        // The arm that leads: the right one unless mirrored.
        int p = cue.Mirror ? 1 : -1;
        // Bobbing and hopping need the feet free.
        float whole = Allowance(GestureReach.Whole) / MathF.Max(Allowance(GestureInfo.Reach(cue.Gesture)), 0.001f);
        var left = _rig.Left;
        var up = _rig.Up;
        var forward = _rig.Forward;

        switch (cue.Gesture)
        {
            case Gesture.Nod:
            {
                float nod = 0.5f - 0.5f * MathF.Cos(u * Mathf.Tau * 2f);
                Head(0.24f * a * nod * w, 0f, 0f);
                break;
            }
            case Gesture.Shake:
                Head(0.04f * w, 0.38f * a * MathF.Sin(u * Mathf.Tau * 2.5f) * w, 0f);
                break;
            case Gesture.Tilt:
                Head(0.05f * w, -p * 0.1f * w, -p * 0.32f * a * w);
                _rig.Rotate("Spine", forward, -p * 0.05f * w);
                break;
            case Gesture.Wave:
            {
                float swing = MathF.Sin(t * Mathf.Tau * 2.2f);
                Arm(p, 1.45f * a, 0.25f, 0.15f, 0f, 1.35f + 0.35f * swing, w);
                Head(0f, p * 0.1f * w, -p * 0.12f * w);
                break;
            }
            case Gesture.Bow:
            {
                float bow = MathF.Pow(MathF.Sin(MathF.PI * u), 0.6f) * a;
                _rig.Rotate("Spine", left, 0.45f * bow * w);
                _rig.Rotate("Chest", left, 0.2f * bow * w);
                _rig.Rotate("Head", left, 0.25f * bow * w);
                Arm(1, 0f, 0.25f * bow, 0.3f, 0.25f, 0f, w);
                Arm(-1, 0f, 0.25f * bow, 0.3f, 0.25f, 0f, w);
                break;
            }
            case Gesture.Cheer:
            {
                float pump = MathF.Sin(u * Mathf.Tau * 2f);
                Arm(1, 2.35f * a + 0.2f * pump, 0.2f, 0.2f, 0f, 0f, w);
                Arm(-1, 2.35f * a - 0.2f * pump, 0.2f, 0.2f, 0f, 0f, w);
                Head(-0.15f * w, 0f, 0.08f * pump * w);
                _rig.OffsetHips(up * (0.02f * _legScale * MathF.Max(0f, pump) * w * whole));
                break;
            }
            case Gesture.Clap:
            {
                float together = 0.5f + 0.5f * MathF.Cos(u * Mathf.Tau * 3.5f);
                Arm(1, 0f, 1.0f, 0.9f, 0.15f + 0.3f * together, 0f, w);
                Arm(-1, 0f, 1.0f, 0.9f, 0.15f + 0.3f * together, 0f, w);
                Head(0.05f * together * w, 0f, 0f);
                break;
            }
            case Gesture.ThumbsUp:
            {
                float pump = 0.15f * MathF.Sin(Mathf.Clamp(u * 2f, 0f, 1f) * MathF.PI);
                Arm(p, 0.25f, 0.75f + pump, 1.45f, 0.1f, 0f, w);
                Head(0.08f * w, -p * 0.08f * w, -p * 0.1f * w);
                break;
            }
            case Gesture.Think:
            {
                Arm(p, 0.1f, 0.95f, 2.45f, 0.5f, 0f, w);
                Arm(-p, 0f, 0.45f, 1.35f, 0.55f, 0f, w);
                float drift = MathF.Sin(t * 1.3f);
                Head(-0.18f * w, -p * (0.2f + 0.08f * drift) * w, p * 0.12f * w);
                break;
            }
            case Gesture.Shrug:
            {
                float s = MathF.Sin(MathF.PI * u);
                _rig.Rotate("LeftShoulder", forward, 0.18f * a * s * w);
                _rig.Rotate("RightShoulder", forward, -0.18f * a * s * w);
                Arm(1, 0.12f * s, 0.25f, 1.2f, -0.3f * s, 0f, w);
                Arm(-1, 0.12f * s, 0.25f, 1.2f, -0.3f * s, 0f, w);
                Head(0f, 0f, -p * 0.18f * s * w);
                break;
            }
            case Gesture.Point:
                Arm(p, 0.55f, 1.35f * a, 0.1f, -0.25f, 0f, w);
                _rig.Rotate("Spine", up, -p * 0.12f * w);
                Head(0f, -p * 0.3f * w, 0f);
                break;
            case Gesture.Count:
            {
                int n = Math.Max(1, cue.Count);
                float beat = (u * (n + 0.5f)) % 1f;
                float bounce = MathF.Exp(-beat * 6f) * MathF.Sin(beat * MathF.PI * 2f);
                Arm(p, 0.35f, 0.65f + 0.12f * bounce, 1.65f, 0.15f, 0f, w);
                Head(0.1f * bounce * w, -p * 0.12f * w, 0f);
                break;
            }
            case Gesture.Explain:
            {
                float s1 = MathF.Sin(u * Mathf.Tau * 1.5f);
                float s2 = MathF.Sin(u * Mathf.Tau * 1.5f + 1.6f);
                Arm(p, 0.2f, 0.55f + 0.15f * s1, 1.0f, -0.25f - 0.3f * MathF.Max(0f, s1) * a, 0f, w);
                Arm(-p, 0.15f, 0.35f + 0.1f * s2, 0.8f, -0.15f - 0.2f * MathF.Max(0f, s2) * a, 0f, w);
                Head(0.06f * MathF.Max(0f, s1) * w, -p * 0.06f * w, 0f);
                break;
            }
            case Gesture.FistPump:
            {
                float pull = Smooth(Mathf.Clamp((u - 0.35f) / 0.2f, 0f, 1f));
                Arm(p, 0.35f, 0.9f - 0.65f * pull, 1.9f, 0.1f, 0f, w);
                _rig.Rotate("Spine", left, 0.1f * pull * w);
                Head(0.08f * pull * w, 0f, 0f);
                break;
            }
            case Gesture.Facepalm:
            {
                Arm(p, 0.1f, 0.85f, 2.35f, 0.5f, 0f, w);
                float shake = MathF.Sin(t * 5f) * Smooth(Mathf.Clamp((u - 0.3f) * 3f, 0f, 1f));
                Head(0.32f * w, 0.1f * shake * w, 0f);
                _rig.Rotate("Spine", left, 0.1f * w);
                break;
            }
            case Gesture.Shy:
            {
                float sway = MathF.Sin(t * 3.2f);
                Arm(1, 0f, 0.3f, 0.55f, 0.35f, 0f, w);
                Arm(-1, 0f, 0.3f, 0.55f, 0.35f, 0f, w);
                _rig.Rotate("LeftShoulder", forward, 0.1f * w);
                _rig.Rotate("RightShoulder", forward, -0.1f * w);
                _rig.Rotate("Hips", forward, 0.05f * sway * w);
                _rig.Rotate("Spine", forward, -0.07f * sway * w);
                Head(0.2f * w, p * 0.3f * a * w, p * 0.12f * w);
                break;
            }
            case Gesture.Pout:
            {
                float hmph = MathF.Exp(-t * 4f) * MathF.Sin(t * 14f);
                Arm(1, 0.1f, 0.6f, 1.95f, 0.6f, 0f, w);
                Arm(-1, 0.1f, 0.6f, 1.95f, 0.6f, 0f, w);
                Head(-0.12f * w + 0.06f * hmph * w, p * 0.45f * a * w, 0f);
                _rig.Rotate("Spine", up, p * 0.12f * w);
                break;
            }
            case Gesture.Startle:
            {
                float jolt = MathF.Exp(-t * 5f) * MathF.Min(1f, t * 20f);
                Arm(1, 0.9f * jolt, 0.3f * jolt, 1.0f * jolt, 0f, 0f, w);
                Arm(-1, 0.9f * jolt, 0.3f * jolt, 1.0f * jolt, 0f, 0f, w);
                _rig.Rotate("Spine", left, -0.18f * jolt * w);
                Head(-0.15f * jolt * w, 0f, 0f);
                _rig.OffsetHips(up * (0.05f * _legScale * MathF.Max(0f, MathF.Sin(MathF.Min(t * 6f, MathF.PI))) * w));
                break;
            }
            case Gesture.Giggle:
            {
                float shake = MathF.Sin(t * 26f) * 0.5f * (1f + MathF.Sin(t * 5f));
                Arm(p, 0.1f, 0.6f, 2.2f, 0.45f, 0f, w);
                _rig.Rotate("Chest", left, 0.04f * shake * w);
                _rig.Rotate("LeftShoulder", forward, 0.04f * shake * w);
                _rig.Rotate("RightShoulder", forward, -0.04f * shake * w);
                Head(0.08f * w, 0f, -p * 0.15f * w);
                break;
            }
            case Gesture.Hop:
            {
                float phase = u * 2f % 1f;
                float lift = MathF.Max(0f, MathF.Sin(phase * Mathf.Tau));
                float crouch = MathF.Max(0f, -MathF.Sin(phase * Mathf.Tau));
                _rig.OffsetHips(up * ((0.09f * lift * a - 0.05f * crouch) * _legScale * w));
                _rig.Rotate("LeftUpperLeg", left, -0.35f * crouch * w);
                _rig.Rotate("RightUpperLeg", left, -0.35f * crouch * w);
                _rig.Rotate("LeftLowerLeg", left, (0.7f * crouch + 0.3f * lift) * w);
                _rig.Rotate("RightLowerLeg", left, (0.7f * crouch + 0.3f * lift) * w);
                Arm(1, 0.25f + 0.45f * lift, 0f, 0.5f, 0f, 0f, w);
                Arm(-1, 0.25f + 0.45f * lift, 0f, 0.5f, 0f, 0f, w);
                break;
            }
            case Gesture.Droop:
            {
                _rig.Rotate("LeftShoulder", forward, -0.1f * w);
                _rig.Rotate("RightShoulder", forward, 0.1f * w);
                _rig.Rotate("Spine", left, 0.18f * w);
                Arm(1, -0.1f, 0.15f, 0.25f, 0.2f, 0f, w);
                Arm(-1, -0.1f, 0.15f, 0.25f, 0.2f, 0f, w);
                Head(0.38f * a * w, 0.08f * MathF.Sin(t * 1.5f) * w, 0f);
                break;
            }
            case Gesture.Sigh:
            {
                float inhale = MathF.Sin(Mathf.Clamp(u / 0.45f, 0f, 1f) * MathF.PI * 0.5f);
                float exhale = Smooth(Mathf.Clamp((u - 0.45f) / 0.3f, 0f, 1f));
                float s = inhale * (1f - exhale);
                _rig.Rotate("LeftShoulder", forward, (0.15f * s - 0.06f * exhale) * w);
                _rig.Rotate("RightShoulder", forward, -(0.15f * s - 0.06f * exhale) * w);
                _rig.Rotate("Chest", left, -0.08f * s * w);
                Head(-0.15f * s * w + 0.2f * exhale * w, 0f, 0f);
                break;
            }
            case Gesture.Idea:
            {
                float pop = MathF.Exp(-t * 4f) * MathF.Min(1f, t * 12f);
                Arm(p, 0.6f, 0.7f, 2.0f, -0.1f, 0f, w);
                Head(-0.15f * w, -p * 0.1f * w, 0f);
                _rig.OffsetHips(up * (0.04f * _legScale * pop * w * whole));
                break;
            }
            case Gesture.Stretch:
            {
                float s = MathF.Sin(MathF.PI * u);
                float bend = MathF.Sin(u * Mathf.Tau) * Smooth(Mathf.Clamp((u - 0.25f) * 4f, 0f, 1f));
                Arm(1, 0.3f, 2.85f * s, 0.2f, 0f, 0f, w);
                Arm(-1, 0.3f, 2.85f * s, 0.2f, 0f, 0f, w);
                _rig.Rotate("Spine", left, -0.12f * s * w);
                _rig.Rotate("Spine", forward, 0.12f * bend * a * w);
                Head(-0.2f * s * w, 0f, 0f);
                _rig.OffsetHips(up * (0.02f * _legScale * s * w * whole));
                break;
            }
            case Gesture.LookAround:
            {
                float yaw = 0.6f * MathF.Sin(u * Mathf.Tau) * a;
                float pitch = -0.12f * MathF.Sin(u * Mathf.Tau * 2f);
                Head(pitch * w, yaw * w, 0f);
                _rig.Rotate("Spine", up, 0.12f * yaw * w);
                break;
            }
            case Gesture.FixHair:
            {
                float brush = MathF.Sin(t * 4f);
                Arm(p, 1.7f, 0.7f, 2.0f + 0.15f * brush, 0.2f, 0f, w);
                Head(0.05f * w, 0f, p * 0.18f * w);
                break;
            }
            case Gesture.Yawn:
            {
                float s = MathF.Sin(MathF.PI * Mathf.Clamp(u * 1.25f, 0f, 1f));
                Arm(p, 0.1f, 0.6f, 2.2f, 0.45f, 0f, w * s);
                Arm(-p, 0.25f * s, 0.15f, 0.4f, 0f, 0f, w);
                Head(-0.22f * s * w, 0f, -p * 0.1f * s * w);
                _rig.Rotate("Chest", left, -0.06f * s * w);
                break;
            }
            case Gesture.ShiftWeight:
            {
                float s = MathF.Sin(MathF.PI * u);
                _rig.OffsetHips(left * (p * 0.035f * _legScale * s * w));
                _rig.Rotate("Hips", forward, -p * 0.07f * s * w);
                _rig.Rotate("Spine", forward, p * 0.09f * s * w);
                string relaxed = p > 0 ? "RightLowerLeg" : "LeftLowerLeg";
                string relaxedHip = p > 0 ? "RightUpperLeg" : "LeftUpperLeg";
                _rig.Rotate(relaxedHip, left, -0.15f * s * w);
                _rig.Rotate(relaxed, left, 0.3f * s * w);
                Head(0f, 0f, p * 0.08f * s * w);
                break;
            }
            case Gesture.KickLegs:
            {
                float s = MathF.Sin(u * Mathf.Tau * 3f);
                _rig.Rotate("LeftLowerLeg", left, -0.55f * MathF.Max(0f, s) * a * w);
                _rig.Rotate("RightLowerLeg", left, -0.55f * MathF.Max(0f, -s) * a * w);
                _rig.Rotate("Spine", left, -0.06f * w);
                Head(-0.06f * w, 0.1f * s * w, 0f);
                break;
            }
        }
    }

    /// <summary>The mood a gesture shows while it plays (when no other mood is set).</summary>
    private static (string Expression, float Weight)? GestureFace(Gesture gesture) => gesture switch
    {
        Gesture.Cheer or Gesture.Clap or Gesture.Hop or Gesture.ThumbsUp or Gesture.Wave or Gesture.Giggle => ("happy", 0.7f),
        Gesture.Pout => ("angry", 0.6f),
        Gesture.Droop or Gesture.Facepalm => ("sad", 0.5f),
        Gesture.Sigh or Gesture.Stretch => ("relaxed", 0.6f),
        Gesture.Shy => ("happy", 0.4f),
        _ => null,
    };

    /// <summary>
    /// Moves one arm relative to hanging at the side: <paramref name="raise"/> lifts it
    /// out sideways, <paramref name="forward"/> swings it to the front, <paramref name="inward"/>
    /// brings it across the body, <paramref name="elbow"/> bends the forearm forward and
    /// <paramref name="forearmUp"/> turns the forearm upward in the screen plane.
    /// </summary>
    private void Arm(int side, float raise, float forward, float elbow, float inward, float forearmUp, float w)
    {
        bool isLeft = side > 0;
        string upper = isLeft ? "LeftUpperArm" : "RightUpperArm";
        string lower = isLeft ? "LeftLowerArm" : "RightLowerArm";
        _rig.Rotate(upper, _rig.Forward, side * raise * w);
        _rig.Rotate(upper, _rig.Left, -forward * w);
        _rig.Rotate(upper, _rig.Up, -side * inward * w);
        _rig.Rotate(lower, isLeft ? _rig.LeftElbowHinge : _rig.RightElbowHinge, elbow * w);
        _rig.Rotate(lower, _rig.Forward, side * forearmUp * w);
    }

    /// <summary>Head and neck: <paramref name="nod"/> down, <paramref name="yaw"/> to its left, <paramref name="tilt"/> to its right.</summary>
    private void Head(float nod, float yaw, float tilt)
    {
        _rig.Rotate("Neck", _rig.Left, 0.35f * nod);
        _rig.Rotate("Head", _rig.Left, 0.65f * nod);
        _rig.Rotate("Neck", _rig.Up, 0.35f * yaw);
        _rig.Rotate("Head", _rig.Up, 0.65f * yaw);
        _rig.Rotate("Head", _rig.Forward, tilt);
    }

    private static float Smooth(float x) => x * x * (3f - 2f * x);
}
