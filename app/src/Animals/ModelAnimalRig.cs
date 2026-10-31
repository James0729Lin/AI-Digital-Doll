using System;
using Godot;

namespace DigitalDoll.Animals;

/// <summary>
/// Poses the bones of a skinned four-legged model (Quaternius' low-poly animals) on top of
/// its clips, as the animals' only clips are standing and walking. Behaviours set
/// <see cref="Channel"/>s in the character's own axes (X its left, Y up, Z ahead; a positive
/// pitch tips the nose down); they are low-passed, so every change of pose flows, then
/// applied after the clip. A leg that is "planted" keeps its paw on the ground wherever
/// the body goes (two-bone IK in the side view): sitting and lying really fold the legs.
/// </summary>
public sealed class ModelAnimalRig
{
    public enum Channel
    {
        HipsY, HipsZ, HipsPitch, HipsRoll, HipsYaw,
        ChestPitch, ChestYaw, ChestRoll,
        NeckPitch, NeckYaw,
        HeadPitch, HeadYaw, HeadRoll,
        TailLift, TailSway, TailWag, TailSpeed,
        // Each of these four: front-left, front-right, back-left, back-right.
        Plant0, Plant1, Plant2, Plant3,
        FootY0, FootY1, FootY2, FootY3,
        FootZ0, FootZ1, FootZ2, FootZ3,
        Swing0, Swing1, Swing2, Swing3,
        Count,
    }

    private sealed class Leg
    {
        public int Upper, Lower, Paw;
        public Vector2 Joint, Ankle;
        public float UpperLength, LowerLength;
        public float UpperAngle, LowerAngle;
        public float Bend;
        public bool Front;
    }

    private readonly Skeleton3D _skeleton;
    private readonly Basis _toCharacter;
    private readonly int _hips, _chest, _neck, _head;
    private readonly int[] _tail;
    private readonly Leg[] _legs;
    private readonly Vector2 _hipsAt, _chestAt;
    private readonly Basis[] _axes;
    private readonly Quaternion[] _restRotation;
    private readonly Vector3 _restHips;
    private readonly float[] _target = new float[(int)Channel.Count];
    private readonly float[] _now = new float[(int)Channel.Count];
    private float _tailPhase;

    private ModelAnimalRig(Skeleton3D skeleton, Transform3D toCharacter, int[] spine, int[] tail, int[][] legs)
    {
        _skeleton = skeleton;
        _toCharacter = toCharacter.Basis;
        (_hips, _chest, _neck, _head) = (spine[0], spine[1], spine[2], spine[3]);
        _tail = tail;

        int count = skeleton.GetBoneCount();
        _axes = new Basis[count];
        _restRotation = new Quaternion[count];
        for (int i = 0; i < count; i++)
        {
            _axes[i] = (toCharacter.Basis * skeleton.GetBoneGlobalRest(i).Basis).Orthonormalized();
            _restRotation[i] = skeleton.GetBoneRest(i).Basis.GetRotationQuaternion();
        }
        _restHips = skeleton.GetBoneRest(_hips).Origin;
        Vector3 At(int bone) => toCharacter * skeleton.GetBoneGlobalRest(bone).Origin;
        static Vector2 Side(Vector3 v) => new(v.Y, v.Z);
        _hipsAt = Side(At(_hips));
        _chestAt = Side(At(_chest));

        // The legs in order, by which side of the body they are on (+X is its left).
        var ordered = new Leg[4];
        foreach (var bones in legs)
        {
            var joint = At(bones[0]);
            var knee = At(bones[1]);
            var ankle = At(bones[2]);
            bool front = joint.Z > (_hipsAt.Y + _chestAt.Y) * 0.5f;
            var leg = new Leg
            {
                Upper = bones[0],
                Lower = bones[1],
                Paw = bones[2],
                Joint = Side(joint),
                Ankle = Side(ankle),
                UpperLength = joint.DistanceTo(knee),
                LowerLength = knee.DistanceTo(ankle),
                UpperAngle = Angle(Side(knee) - Side(joint)),
                LowerAngle = Angle(Side(ankle) - Side(knee)),
                // Elbows point back, knees forward.
                Bend = front ? -1f : 1f,
                Front = front,
            };
            ordered[(front ? 0 : 2) + (joint.X > 0f ? 0 : 1)] = leg;
        }
        _legs = ordered;
        for (int i = 0; i < 4; i++)
            _legs[i] ??= ordered[i ^ 1];
    }

    /// <summary>The rig for a model with this skeleton's bones, or null when it has others.</summary>
    public static ModelAnimalRig? TryCreate(Node3D model, Node3D space)
    {
        if (model.FindChildren("*", "Skeleton3D", true, false) is not { Count: > 0 } found || found[0] is not Skeleton3D skeleton)
            return null;
        int[]? Bones(params string[] names)
        {
            var bones = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                bones[i] = skeleton.FindBone(names[i]);
                if (bones[i] < 0)
                    return null;
            }
            return bones;
        }
        var spine = Bones("Bone", "Bone.001", "Bone.002", "Bone.003");
        var tail = Bones("Bone.004", "Bone.005", "Bone.006", "Bone.007");
        var a = Bones("Bone.014", "Bone.015", "Bone.016");
        var b = Bones("Bone.017", "Bone.018", "Bone.019");
        var c = Bones("Bone.008", "Bone.009", "Bone.010");
        var d = Bones("Bone.011", "Bone.012", "Bone.013");
        if (spine == null || tail == null || a == null || b == null || c == null || d == null)
            return null;
        var toSpace = skeleton.Transform;
        for (var parent = skeleton.GetParent() as Node3D; parent != null && parent != space; parent = parent.GetParent() as Node3D)
            toSpace = parent.Transform * toSpace;
        return new ModelAnimalRig(skeleton, toSpace, spine, tail, new[] { a, b, c, d });
    }

    /// <summary>Starts a frame: nothing asked for yet.</summary>
    public void Clear() => Array.Clear(_target);

    public void Add(Channel channel, float amount) => _target[(int)channel] += amount;

    public void AddLegs(Channel first, float amount)
    {
        for (int i = 0; i < 4; i++)
            _target[(int)first + i] += amount;
    }

    public void AddLeg(Channel first, int leg, float amount) => _target[(int)first + leg] += amount;

    /// <summary>Back to rest before the clip plays, as the clip may not touch every bone.</summary>
    public void Reset()
    {
        foreach (int bone in new[] { _hips, _chest, _neck, _head })
            _skeleton.SetBonePoseRotation(bone, _restRotation[bone]);
        _skeleton.SetBonePosePosition(_hips, _restHips);
        foreach (int bone in _tail)
            _skeleton.SetBonePoseRotation(bone, _restRotation[bone]);
        foreach (var leg in _legs)
        {
            _skeleton.SetBonePoseRotation(leg.Upper, _restRotation[leg.Upper]);
            _skeleton.SetBonePoseRotation(leg.Lower, _restRotation[leg.Lower]);
            _skeleton.SetBonePoseRotation(leg.Paw, _restRotation[leg.Paw]);
        }
    }

    /// <summary>Applies the channels over what the clip did this frame.</summary>
    public void Apply(float dt, float height)
    {
        float k = 1f - MathF.Exp(-14f * dt);
        for (int i = 0; i < _now.Length; i++)
            _now[i] += (_target[i] - _now[i]) * k;

        float hipsPitch = Now(Channel.HipsPitch), chestPitch = Now(Channel.ChestPitch);
        var move = new Vector3(0f, Now(Channel.HipsY), Now(Channel.HipsZ)) * height;
        _skeleton.SetBonePosePosition(_hips, _skeleton.GetBonePosePosition(_hips) + _toCharacter.Inverse() * move);
        Turn(_hips, hipsPitch, Now(Channel.HipsYaw), Now(Channel.HipsRoll));
        Turn(_chest, chestPitch, Now(Channel.ChestYaw), Now(Channel.ChestRoll));
        Turn(_neck, Now(Channel.NeckPitch), Now(Channel.NeckYaw), 0f);
        Turn(_head, Now(Channel.HeadPitch), Now(Channel.HeadYaw), Now(Channel.HeadRoll));

        // The tail: a wave running down it, each segment a little behind the one before.
        _tailPhase = (_tailPhase + dt * Now(Channel.TailSpeed)) % Mathf.Tau;
        for (int i = 0; i < _tail.Length; i++)
        {
            float along = (i + 1f) / _tail.Length;
            float sway = Now(Channel.TailSway) + Now(Channel.TailWag) * MathF.Sin(_tailPhase - i * 0.75f) * along;
            Turn(_tail[i], Now(Channel.TailLift) / _tail.Length, sway / _tail.Length * 1.6f, 0f);
        }

        // Where each hip or shoulder has gone, side view: (height, ahead).
        var hips = _hipsAt + new Vector2(move.Y, move.Z);
        var chest = hips + Pitched(_chestAt - _hipsAt, hipsPitch);
        for (int i = 0; i < _legs.Length; i++)
        {
            var leg = _legs[i];
            float parent = leg.Front ? hipsPitch + chestPitch : hipsPitch;
            var joint = leg.Front ? chest + Pitched(leg.Joint - _chestAt, parent) : hips + Pitched(leg.Joint - _hipsAt, parent);
            PoseLeg(leg, joint, parent, i, height);
        }
    }

    private float Now(Channel channel) => _now[(int)channel];

    private void PoseLeg(Leg leg, Vector2 joint, float parent, int index, float height)
    {
        float plant = Mathf.Clamp(_now[(int)Channel.Plant0 + index], 0f, 1f);
        float swing = _now[(int)Channel.Swing0 + index];
        if (plant > 0.001f)
        {
            var foot = leg.Ankle + new Vector2(_now[(int)Channel.FootY0 + index], _now[(int)Channel.FootZ0 + index]) * height;
            Solve(leg, joint, foot, out float upper, out float lower);
            // Each bone's turn from its rest pose, given what its parent has already done.
            float dUpper = Mathf.AngleDifference(leg.UpperAngle, upper);
            float dLower = Mathf.AngleDifference(leg.LowerAngle, lower);
            Blend(leg.Upper, dUpper - parent, plant);
            Blend(leg.Lower, dLower - dUpper, plant);
            // The paw stays level with the ground.
            Blend(leg.Paw, -dLower, plant);
        }
        if (MathF.Abs(swing) > 0.0001f)
            Turn(leg.Upper, swing, 0f, 0f);
    }

    /// <summary>The two bones' directions that bring the ankle to the foot's place, bending the natural way.</summary>
    private static void Solve(Leg leg, Vector2 joint, Vector2 foot, out float upper, out float lower)
    {
        float a = leg.UpperLength, b = leg.LowerLength;
        var to = foot - joint;
        float d = Mathf.Clamp(to.Length(), MathF.Abs(a - b) + 0.001f, a + b - 0.001f);
        float towards = Angle(to);
        float inner = MathF.Acos(Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f));
        upper = towards + leg.Bend * inner;
        var knee = joint + new Vector2(MathF.Cos(upper), MathF.Sin(upper)) * a;
        var end = joint + to.Normalized() * d;
        lower = Angle(end - knee);
    }

    /// <summary>Direction in the side view (height, ahead); turning about X by t adds t to it.</summary>
    private static float Angle(Vector2 v) => MathF.Atan2(v.Y, v.X);

    private static Vector2 Pitched(Vector2 v, float pitch)
    {
        float c = MathF.Cos(pitch), s = MathF.Sin(pitch);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    /// <summary>Turns a bone (in the character's axes) on top of its pose now.</summary>
    private void Turn(int bone, float pitch, float yaw, float roll)
    {
        var turn = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, pitch) * new Basis(Vector3.Back, roll);
        var axes = _axes[bone];
        _skeleton.SetBonePoseRotation(bone, _skeleton.GetBonePoseRotation(bone) * (axes.Inverse() * turn * axes).GetRotationQuaternion());
    }

    /// <summary>Between the clip's pose and this turn (about X, in the character's axes) from the rest pose.</summary>
    private void Blend(int bone, float pitch, float weight)
    {
        var axes = _axes[bone];
        var posed = _restRotation[bone] * (axes.Inverse() * new Basis(Vector3.Right, pitch) * axes).GetRotationQuaternion();
        _skeleton.SetBonePoseRotation(bone, _skeleton.GetBonePoseRotation(bone).Slerp(posed, weight));
    }
}
