using System;
using System.Collections.Generic;
using DigitalDoll.Core.Acting;
using Godot;

namespace DigitalDoll.Character;

/// <summary>
/// Held poses (<see cref="PoseCatalog"/>): each one a function of its weight and the
/// time it has been held, built from the same arm, head and leg moves as the gestures.
/// Arms are given relative to hanging at the side (see <see cref="Arm"/>); the right
/// arm leads unless a pose says otherwise.
/// </summary>
public sealed partial class ProceduralAnimator
{
    private sealed record PoseDef(Action<ProceduralAnimator, float, float> Apply, float Yaw = 0.3f, string? Face = "happy", float FaceWeight = 0.5f);

    private const int R = -1, L = 1;

    private static readonly Dictionary<string, PoseDef> PoseDefs = new()
    {
        // ---- Cute gestures -------------------------------------------------------
        ["heart_hands"] = new((a, w, t) =>
        {
            a.Arm(R, 0.25f, 0.95f, 1.55f, 0.65f, 0f, w);
            a.Arm(L, 0.25f, 0.95f, 1.55f, 0.65f, 0f, w);
            a.Head(0.05f * w, 0f, 0.12f * w);
        }, Yaw: 0f),
        ["big_heart"] = new((a, w, t) =>
        {
            a.Arm(R, 2.55f, 0.25f, 1.25f, 0.2f, 0.6f, w);
            a.Arm(L, 2.55f, 0.25f, 1.25f, 0.2f, 0.6f, w);
            a.Head(-0.05f * w, 0f, -0.1f * w);
        }, Yaw: 0f, FaceWeight: 0.8f),
        ["finger_heart"] = new((a, w, t) =>
        {
            a.Arm(R, 0.35f, 1.0f, 2.2f, 0.45f, 0f, w);
            a.Head(0.04f * w, -0.08f * w, -0.18f * w);
        }, Yaw: 0.15f),
        ["double_peace"] = new((a, w, t) =>
        {
            a.Arm(R, 0.2f, 0.95f, 2.3f, 0.75f, 0f, w);
            a.Arm(L, 0.2f, 0.95f, 2.3f, 0.75f, 0f, w);
            a.Head(-0.06f * w, 0f, 0.1f * w);
        }, Yaw: 0f, FaceWeight: 0.9f),
        ["gal_peace"] = new((a, w, t) =>
        {
            a.Arm(R, 0.55f, 1.15f, 1.95f, 0.55f, 0f, w);
            a.Head(0f, -0.1f * w, -0.16f * w);
            a.Lean(0.04f * w, w);
        }, Yaw: 0.15f, FaceWeight: 0.8f),
        ["cheek_fists"] = new((a, w, t) =>
        {
            float sway = MathF.Sin(t * 3f);
            a.Arm(R, 0.25f, 0.85f, 2.45f, 0.62f, 0f, w);
            a.Arm(L, 0.25f, 0.85f, 2.45f, 0.62f, 0f, w);
            a.Head(0.05f * w, 0f, 0.14f * sway * w);
            a.Lean(0.04f * sway * w, w);
        }, Yaw: 0f, FaceWeight: 0.9f),
        ["bunny_ears"] = new((a, w, t) =>
        {
            float flap = 0.12f * MathF.Sin(t * 6f);
            a.Arm(R, 2.25f, 0.35f, 1.95f + flap, 0.15f, 0f, w);
            a.Arm(L, 2.25f, 0.35f, 1.95f - flap, 0.15f, 0f, w);
            a.Head(0.04f * w, 0f, 0.1f * w);
        }, Yaw: 0f, FaceWeight: 0.9f),
        ["cat_paw"] = new((a, w, t) =>
        {
            float beckon = 0.25f * MathF.Max(0f, MathF.Sin(t * 5f));
            a.Arm(R, 0.85f, 0.65f, 2.0f + beckon, 0.2f, 0f, w);
            a.Arm(L, 0.1f, 0.6f, 1.6f, 0.55f, 0f, w);
            a.Head(0f, -0.06f * w, -0.14f * w);
        }, Yaw: 0.2f, FaceWeight: 0.8f),
        ["rawr"] = new((a, w, t) =>
        {
            a.Arm(R, 0.55f, 1.0f, 1.9f, 0.2f, 0f, w);
            a.Arm(L, 0.55f, 1.0f, 1.9f, 0.2f, 0f, w);
            a._rig.Rotate("Spine", a._rig.Left, 0.18f * w);
            a.Head(-0.1f * w, 0f, 0f);
            a.Crouch(0.25f, w);
        }, Yaw: 0f, Face: "angry", FaceWeight: 0.35f),
        ["shh"] = new((a, w, t) =>
        {
            a.Arm(R, 0.18f, 1.0f, 2.45f, 0.78f, 0f, w);
            a.Head(0.06f * w, 0f, -0.08f * w);
            a.Lean(0.05f * w, w);
        }, Yaw: 0.15f, Face: "relaxed", FaceWeight: 0.5f),
        ["wink"] = new((a, w, t) =>
        {
            a.Arm(R, 0.35f, 1.05f, 2.3f, 0.5f, 0f, w);
            a.Head(0.02f * w, -0.05f * w, -0.24f * w);
        }, Yaw: 0.15f, Face: "blinkRight", FaceWeight: 1f),
        ["blow_kiss"] = new((a, w, t) =>
        {
            // Fingers to the lips, then thrown out toward the viewer, over and over.
            float k = 0.5f - 0.5f * MathF.Cos(MathF.Min(t, 99f) * 2.4f);
            a.Arm(R, 0.2f + 0.3f * k, 1.0f + 0.4f * k, 2.4f - 1.7f * k, 0.75f - 0.6f * k, 0f, w);
            a.Head(0.04f * w, 0f, -0.08f * w);
        }, Yaw: 0.1f, Face: "blinkRight", FaceWeight: 0.6f),
        ["flower_chin"] = new((a, w, t) =>
        {
            a.Arm(R, 0.12f, 0.95f, 2.4f, 0.8f, 0f, w);
            a.Arm(L, 0.12f, 0.95f, 2.4f, 0.8f, 0f, w);
            a.Head(-0.08f * w, 0f, 0.12f * MathF.Sin(t * 1.5f) * w);
            a.Lean(0.06f * w, w);
        }, Yaw: 0f, FaceWeight: 0.9f),
        ["poke_cheek"] = new((a, w, t) =>
        {
            a.Arm(R, 0.3f, 1.0f, 2.35f, 0.5f, 0f, w);
            a.Head(0.02f * w, -0.06f * w, 0.2f * w);
        }, Yaw: 0.1f, FaceWeight: 0.8f),
        ["cover_mouth"] = new((a, w, t) =>
        {
            a.Arm(R, 0.12f, 1.05f, 2.4f, 0.8f, 0f, w);
            a.Arm(L, 0.12f, 1.05f, 2.4f, 0.8f, 0f, w);
            a.Shrug(0.08f, w);
            a.Head(0.12f * w, 0.1f * w, 0f);
        }, Yaw: 0.2f, Face: "happy", FaceWeight: 0.4f),
        ["pray"] = new((a, w, t) =>
        {
            a.Arm(R, 0f, 0.9f, 1.55f, 0.78f, 0f, w);
            a.Arm(L, 0f, 0.9f, 1.55f, 0.78f, 0f, w);
            a.Head(0.12f * w, 0f, 0.14f * w);
            a.Lean(0.08f * w, w);
        }, Yaw: 0f, Face: "sad", FaceWeight: 0.3f),
        ["hug_self"] = new((a, w, t) =>
        {
            float rock = MathF.Sin(t * 2.2f);
            a.Arm(R, 0.25f, 0.6f, 2.2f, 1.05f, 0f, w);
            a.Arm(L, 0.25f, 0.55f, 2.15f, 1.0f, 0f, w);
            a.Shrug(0.12f, w);
            a._rig.Rotate("Spine", a._rig.Up, 0.12f * rock * w);
            a.Head(0.15f * w, 0.1f * rock * w, 0f);
        }, Yaw: 0.25f, Face: "happy", FaceWeight: 0.5f),
        ["finger_frame"] = new((a, w, t) =>
        {
            a.Arm(R, 0.6f, 1.2f, 1.7f, 0.3f, 0f, w);
            a.Arm(L, 0.6f, 1.2f, 1.7f, 0.3f, 0f, w);
            a.Head(-0.04f * w, 0f, -0.1f * w);
        }, Yaw: 0f, FaceWeight: 0.6f),
        ["tuck_hair"] = new((a, w, t) =>
        {
            a.Arm(R, 1.25f, 0.55f, 2.4f, 0.2f, 0f, w);
            a.Head(0.08f * w, 0.08f * w, -0.14f * w);
        }, Yaw: 0.3f, Face: "relaxed", FaceWeight: 0.5f),
        ["cheeks_hands"] = new((a, w, t) =>
        {
            float sway = MathF.Sin(t * 2.4f);
            a.Arm(R, 0.22f, 0.9f, 2.45f, 0.66f, 0f, w);
            a.Arm(L, 0.22f, 0.9f, 2.45f, 0.66f, 0f, w);
            a.Head(0.04f * w, 0.06f * sway * w, 0.1f * sway * w);
        }, Yaw: 0f, FaceWeight: 0.9f),
        ["salute"] = new((a, w, t) =>
        {
            a.Arm(R, 1.35f, 0.45f, 2.3f, 0.3f, 0f, w);
            a.Head(-0.04f * w, 0f, 0f);
            a.Lean(-0.04f * w, w);
        }, Yaw: 0.1f, FaceWeight: 0.7f),
        ["point_camera"] = new((a, w, t) =>
        {
            a.Arm(R, 0.3f, 1.5f, 0.12f, 0.25f, 0f, w);
            a.HandOnHip(L, w);
            a.Head(-0.04f * w, 0f, -0.1f * w);
            a.Lean(0.06f * w, w);
        }, Yaw: 0.1f, FaceWeight: 0.8f),
        ["heart_chest"] = new((a, w, t) =>
        {
            a.Arm(R, 0.4f, 1.1f, 1.8f, 0.3f, 0f, w);
            a.Arm(L, 0f, 0.5f, 1.85f, 0.85f, 0f, w);
            a.Head(0.02f * w, 0f, -0.12f * w);
        }, Yaw: 0.1f, FaceWeight: 0.8f),
        ["peek_fingers"] = new((a, w, t) =>
        {
            a.Arm(R, 0.22f, 1.1f, 2.3f, 0.7f, 0f, w);
            a.Arm(L, 0.22f, 1.1f, 2.3f, 0.7f, 0f, w);
            a.Shrug(0.1f, w);
            a.Head(0.1f * w, 0.05f * MathF.Sin(t * 1.3f) * w, 0f);
        }, Yaw: 0f, Face: "surprised", FaceWeight: 0.3f),

        // ---- Standing poses ------------------------------------------------------
        ["hand_on_hip"] = new((a, w, t) =>
        {
            a.Contrapposto(R, w);
            a.HandOnHip(R, w);
            a.Head(0f, -0.06f * w, -0.08f * w);
        }, Yaw: 0.35f, FaceWeight: 0.6f),
        ["hands_on_hips"] = new((a, w, t) =>
        {
            a.HandOnHip(R, w);
            a.HandOnHip(L, w);
            a.Lean(-0.06f * w, w);
            a.Head(-0.06f * w, 0f, 0f);
        }, Yaw: 0.1f, FaceWeight: 0.7f),
        ["hair_up"] = new((a, w, t) =>
        {
            a.Arm(R, 2.0f, 0.4f, 2.3f, 0.1f, 0f, w);
            a.Arm(L, 2.0f, 0.4f, 2.3f, 0.1f, 0f, w);
            a._rig.Rotate("Spine", a._rig.Left, -0.08f * w);
            a.Head(-0.08f * w, 0f, 0.1f * w);
            a.Contrapposto(L, w);
        }, Yaw: 0.25f, Face: "relaxed", FaceWeight: 0.5f),
        ["look_back"] = new((a, w, t) =>
        {
            // Back to the viewer, looking over the shoulder.
            a._rig.Rotate("Spine", a._rig.Up, -0.35f * w);
            a._rig.Rotate("Chest", a._rig.Up, -0.3f * w);
            a.Head(0.02f * w, -0.7f * w, 0.1f * w);
            a.Arm(R, 0.1f, -0.15f, 0.3f, -0.1f, 0f, w);
            a.Contrapposto(R, w);
        }, Yaw: 2.4f, FaceWeight: 0.6f),
        ["reach_up"] = new((a, w, t) =>
        {
            float s = 0.5f + 0.5f * MathF.Sin(t * 1.6f);
            a.Arm(R, 2.8f, 0.2f, 0.15f, 0.15f, 0f, w);
            a.Arm(L, 2.8f, 0.2f, 0.15f, 0.15f, 0f, w);
            a._rig.Rotate("Spine", a._rig.Forward, 0.08f * (s - 0.5f) * w);
            a.Head(-0.2f * w, 0f, 0f);
            a.Tiptoe(0.6f * s, w);
        }, Yaw: 0.1f, Face: "relaxed", FaceWeight: 0.6f),
        ["hand_behind_head"] = new((a, w, t) =>
        {
            a.Arm(R, 2.1f, 0.3f, 2.25f, 0.1f, 0f, w);
            a.Contrapposto(R, w);
            a.Head(0f, 0f, 0.12f * w);
        }, Yaw: 0.25f, FaceWeight: 0.6f),
        ["arms_behind"] = new((a, w, t) =>
        {
            a.Arm(R, 0.05f, -0.45f, 0.35f, -0.3f, 0f, w);
            a.Arm(L, 0.05f, -0.45f, 0.35f, -0.3f, 0f, w);
            a._rig.Rotate("Spine", a._rig.Left, -0.08f * w);
            a.Head(0.04f * w, 0f, 0.14f * MathF.Sin(t * 1.4f) * w);
            a.Tiptoe(0.3f * (0.5f + 0.5f * MathF.Sin(t * 2.2f)), w);
        }, Yaw: 0.1f, FaceWeight: 0.7f),
        ["leg_back"] = new((a, w, t) =>
        {
            var left = a._rig.Left;
            a._rig.Rotate("RightUpperLeg", left, 0.25f * w);
            a._rig.Rotate("RightLowerLeg", left, 1.7f * w);
            a._rig.Rotate("RightFoot", left, 0.4f * w);
            a.Arm(R, 0.6f, 0.2f, 0.3f, 0f, 0f, w);
            a.Arm(L, 0.5f, 0.5f, 1.6f, 0.4f, 0f, w);
            a.Head(0f, 0f, -0.12f * w);
        }, Yaw: 0.5f, FaceWeight: 0.8f),
        ["tiptoe_v"] = new((a, w, t) =>
        {
            a.Arm(R, 2.35f, 0.1f, 0.1f, -0.1f, 0f, w);
            a.Arm(L, 2.35f, 0.1f, 0.1f, -0.1f, 0f, w);
            a.Head(-0.15f * w, 0f, 0f);
            a.Tiptoe(1f, w);
        }, Yaw: 0f, FaceWeight: 0.9f),
    };

    private float _pose, _poseTime;
    private string? _poseId;

    public void StrikePose(string id)
    {
        if (!PoseDefs.ContainsKey(id))
            return;
        if (id != _poseId)
            _poseTime = 0f;
        _poseId = id;
    }

    /// <summary>The body turn while holding the current pose (0 faces the viewer).</summary>
    public float PoseYaw(int facing) => _poseId != null && PoseDefs.TryGetValue(_poseId, out var def) ? facing * def.Yaw : 0f;

    private void PosePose()
    {
        if (_pose <= 0.001f || _poseId == null || !PoseDefs.TryGetValue(_poseId, out var def))
            return;
        def.Apply(this, _pose, _poseTime);
    }

    private void PoseFace()
    {
        if (_pose <= 0.01f || _poseId == null || !PoseDefs.TryGetValue(_poseId, out var def) || def.Face == null)
            return;
        _expressions.Set(def.Face, def.FaceWeight * _pose);
    }

    // ---- Building blocks -----------------------------------------------------------

    /// <summary>Leans the upper body forward (positive) or back.</summary>
    private void Lean(float amount, float w) => _rig.Rotate("Spine", _rig.Left, amount);

    private void Shrug(float amount, float w)
    {
        _rig.Rotate("LeftShoulder", _rig.Forward, amount * w);
        _rig.Rotate("RightShoulder", _rig.Forward, -amount * w);
    }

    /// <summary>Weight on one leg: hips pushed toward it, the other knee relaxed.</summary>
    private void Contrapposto(int side, float w)
    {
        var f = _rig.Forward;
        var left = _rig.Left;
        _rig.OffsetHips(left * (side * 0.035f * _legScale * w));
        _rig.Rotate("Hips", f, -side * 0.08f * w);
        _rig.Rotate("Spine", f, side * 0.1f * w);
        string relaxed = side > 0 ? "RightUpperLeg" : "LeftUpperLeg";
        string relaxedKnee = side > 0 ? "RightLowerLeg" : "LeftLowerLeg";
        _rig.Rotate(relaxed, left, -0.15f * w);
        _rig.Rotate(relaxedKnee, left, 0.3f * w);
    }

    /// <summary>A hand on the hip, elbow out to the side.</summary>
    private void HandOnHip(int side, float w)
    {
        Arm(side, 0.4f, 0f, 0.85f, 0f, -1.5f, w);
    }

    /// <summary>Knees bent a little (0–1 of a half crouch), feet kept flat.</summary>
    private void Crouch(float amount, float w)
    {
        var left = _rig.Left;
        float c = amount * w;
        _rig.OffsetHips(_rig.Up * (-0.1f * _legScale * c));
        _rig.Rotate("LeftUpperLeg", left, -0.6f * c);
        _rig.Rotate("RightUpperLeg", left, -0.6f * c);
        _rig.Rotate("LeftLowerLeg", left, 1.1f * c);
        _rig.Rotate("RightLowerLeg", left, 1.1f * c);
        _rig.Rotate("LeftFoot", left, -0.5f * c);
        _rig.Rotate("RightFoot", left, -0.5f * c);
    }

    /// <summary>Up on the toes (0–1).</summary>
    private void Tiptoe(float amount, float w)
    {
        float k = amount * w;
        _rig.OffsetHips(_rig.Up * (0.035f * _legScale * k));
        _rig.Rotate("LeftFoot", _rig.Left, 0.5f * k);
        _rig.Rotate("RightFoot", _rig.Left, 0.5f * k);
    }
}
