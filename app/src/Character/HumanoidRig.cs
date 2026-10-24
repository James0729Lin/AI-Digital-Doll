using System.Collections.Generic;
using Godot;

namespace DigitalDoll.Character;

/// <summary>
/// Poses a VRM humanoid skeleton with rotations expressed in the character's own
/// frame (left / up / forward), so the same procedural animation works on every
/// model regardless of how its bones are oriented.
/// </summary>
public sealed class HumanoidRig
{
    public static readonly string[] AnimatedBones =
    {
        "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
        "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
        "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes",
        "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes",
    };

    private readonly Dictionary<string, int> _bones = new();
    private readonly Dictionary<int, Quaternion> _pending = new();
    private readonly Vector3 _hipsRestPosition;
    private readonly Basis _hipsParentRestInverse = Basis.Identity;
    private Vector3 _hipsOffset;

    private HumanoidRig(Skeleton3D skeleton)
    {
        Skeleton = skeleton;
        foreach (var name in AnimatedBones)
        {
            int index = skeleton.FindBone(name);
            if (index >= 0)
                _bones[name] = index;
        }

        Up = Vector3.Up;
        var left = BoneRestPosition("LeftUpperLeg") - BoneRestPosition("RightUpperLeg");
        left.Y = 0f;
        Left = left.LengthSquared() > 1e-8f ? left.Normalized() : Vector3.Right;
        Forward = Left.Cross(Up).Normalized();

        if (_bones.TryGetValue("Hips", out int hips))
        {
            _hipsRestPosition = skeleton.GetBoneRest(hips).Origin;
            int parent = skeleton.GetBoneParent(hips);
            if (parent >= 0)
                _hipsParentRestInverse = skeleton.GetBoneGlobalRest(parent).Basis.Inverse();
        }

        // T-pose models hold the arms level; A-pose models (MakeHuman) already lower them.
        var arm = BoneRestPosition("LeftLowerArm") - BoneRestPosition("LeftUpperArm");
        ArmDroop = arm.LengthSquared() > 1e-8f ? Mathf.Atan2(-arm.Y, new Vector2(arm.X, arm.Z).Length()) : 0f;

        float footY = Mathf.Min(BoneRestPosition("LeftFoot").Y, BoneRestPosition("RightFoot").Y);
        float headY = BoneRestPosition("Head").Y;
        // The head bone sits around the jaw; the crown is roughly 0.2 m above it on an adult.
        Height = Mathf.Max(headY - footY, 0.3f) * 1.14f;
        FootHeight = footY;
    }

    public Skeleton3D Skeleton { get; }

    /// <summary>Character axes in skeleton space.</summary>
    public Vector3 Left { get; }
    public Vector3 Up { get; }
    public Vector3 Forward { get; }

    /// <summary>Approximate standing height in skeleton units (metres for VRM).</summary>
    public float Height { get; }

    /// <summary>How far the rest pose's upper arms already point below horizontal (radians).</summary>
    public float ArmDroop { get; }

    /// <summary>Rest height of the ankle above the skeleton origin.</summary>
    public float FootHeight { get; }

    public static HumanoidRig? Create(Node root)
    {
        var skeleton = FindSkeleton(root);
        if (skeleton == null || skeleton.FindBone("Hips") < 0)
            return null;
        return new HumanoidRig(skeleton);
    }

    public bool Has(string bone) => _bones.ContainsKey(bone);

    /// <summary>Starts a new pose; every bone is reset to rest when <see cref="Commit"/> runs.</summary>
    public void Begin()
    {
        _pending.Clear();
        _hipsOffset = Vector3.Zero;
    }

    /// <summary>
    /// Rotates a bone about an axis given in the character frame. Calls are applied in
    /// order, and children follow their parent's rotation.
    /// </summary>
    public void Rotate(string bone, Vector3 axis, float radians)
    {
        if (radians == 0f || !_bones.TryGetValue(bone, out int index))
            return;
        var q = new Quaternion(axis.Normalized(), radians);
        _pending[index] = _pending.TryGetValue(index, out var existing) ? q * existing : q;
    }

    /// <summary>Moves the hips in skeleton space (bobbing, crouching).</summary>
    public void OffsetHips(Vector3 offset) => _hipsOffset += offset;

    public void Commit()
    {
        foreach (var (name, index) in _bones)
        {
            var rest = Skeleton.GetBoneRest(index);
            var rotation = rest.Basis.GetRotationQuaternion();
            if (_pending.TryGetValue(index, out var world))
            {
                var restGlobal = Skeleton.GetBoneGlobalRest(index).Basis.GetRotationQuaternion();
                rotation *= restGlobal.Inverse() * world * restGlobal;
            }
            Skeleton.SetBonePoseRotation(index, rotation.Normalized());

            if (name == "Hips")
                Skeleton.SetBonePosePosition(index, _hipsRestPosition + _hipsParentRestInverse * _hipsOffset);
        }
    }

    public Vector3 BoneGlobalPosition(string bone) =>
        _bones.TryGetValue(bone, out int index)
            ? Skeleton.GlobalTransform * Skeleton.GetBoneGlobalPose(index).Origin
            : Skeleton.GlobalPosition;

    private Vector3 BoneRestPosition(string bone)
    {
        int index = Skeleton.FindBone(bone);
        return index >= 0 ? Skeleton.GetBoneGlobalRest(index).Origin : Vector3.Zero;
    }

    private static Skeleton3D? FindSkeleton(Node root)
    {
        if (root is Skeleton3D skeleton)
            return skeleton;
        foreach (var child in root.GetChildren())
        {
            var found = FindSkeleton(child);
            if (found != null)
                return found;
        }
        return null;
    }
}
