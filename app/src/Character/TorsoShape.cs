using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DigitalDoll.Character;

/// <summary>
/// The torso's outline (chest and bust included) measured from a model's skinned meshes
/// in the rest pose: for each height around the chest, how far the body reaches to the
/// front, the back and the sides. Hands and forearms are kept outside it, so arms held
/// close in front of the body rest on the chest rather than passing through it.
/// </summary>
public sealed class TorsoShape
{
    private const float Slice = 0.02f;

    private readonly HumanoidRig _rig;
    private readonly int _bone;
    private readonly Transform3D _restGlobal;
    private readonly float _bottom;
    private readonly float[] _front, _back, _half;

    private TorsoShape(HumanoidRig rig, int bone, float bottom, float[] front, float[] back, float[] half)
    {
        _rig = rig;
        _bone = bone;
        _restGlobal = rig.Skeleton.GetBoneGlobalRest(bone);
        _bottom = bottom;
        _front = front;
        _back = back;
        _half = half;
    }

    /// <summary>Measures the torso, or null when the model has no usable skinned torso.</summary>
    public static TorsoShape? Measure(Node model, HumanoidRig rig)
    {
        var skeleton = rig.Skeleton;
        int bone = skeleton.FindBone("UpperChest");
        if (bone < 0)
            bone = skeleton.FindBone("Chest");
        if (bone < 0)
            return null;
        var torsoBones = new HashSet<int>();
        foreach (var name in new[] { "Spine", "Chest", "UpperChest" })
        {
            int i = skeleton.FindBone(name);
            if (i >= 0)
                torsoBones.Add(i);
        }
        // Breast bones (VRoid "J_Sec_L_Bust1"…) and anything under them.
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            string name = skeleton.GetBoneName(i);
            if (name.Contains("Bust", StringComparison.OrdinalIgnoreCase) || name.Contains("Breast", StringComparison.OrdinalIgnoreCase))
                torsoBones.Add(i);
        }

        var origin = skeleton.GetBoneGlobalRest(bone).Origin;
        var points = new List<Vector3>();
        foreach (var mesh in model.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            if (mesh.Skin is not Skin skin || mesh.Mesh is not Mesh source || mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton) != skeleton)
                continue;
            var bindBones = new int[skin.GetBindCount()];
            var binds = new Transform3D[bindBones.Length];
            for (int b = 0; b < binds.Length; b++)
            {
                int index = skin.GetBindBone(b);
                if (index < 0)
                    index = skeleton.FindBone(skin.GetBindName(b));
                bindBones[b] = index;
                binds[b] = (index >= 0 ? skeleton.GetBoneGlobalRest(index) : Transform3D.Identity) * skin.GetBindPose(b);
            }
            for (int surface = 0; surface < source.GetSurfaceCount(); surface++)
            {
                var arrays = source.SurfaceGetArrays(surface);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                int per = vertices.Length > 0 ? bones.Length / vertices.Length : 0;
                for (int v = 0; v < vertices.Length; v++)
                {
                    // Only vertices mostly moved by the torso (not arms, legs or the skirt).
                    float torso = 0f, total = 0f;
                    var at = Vector3.Zero;
                    for (int k = 0; k < per; k++)
                    {
                        float w = weights[v * per + k];
                        int bind = bones[v * per + k];
                        if (w <= 0f || bind < 0 || bind >= binds.Length)
                            continue;
                        at += binds[bind] * vertices[v] * w;
                        total += w;
                        if (torsoBones.Contains(bindBones[bind]))
                            torso += w;
                    }
                    if (total > 0f && torso / total > 0.6f)
                        points.Add(at / total - origin);
                }
            }
        }
        if (points.Count < 50)
            return null;

        float bottom = points.Min(p => p.Dot(rig.Up));
        float top = points.Max(p => p.Dot(rig.Up));
        int count = Math.Max(1, (int)MathF.Ceiling((top - bottom) / Slice) + 1);
        var front = Enumerable.Repeat(float.MinValue, count).ToArray();
        var back = Enumerable.Repeat(float.MaxValue, count).ToArray();
        var half = new float[count];
        foreach (var p in points)
        {
            int i = (int)((p.Dot(rig.Up) - bottom) / Slice);
            float z = p.Dot(rig.Forward), x = MathF.Abs(p.Dot(rig.Left));
            front[i] = MathF.Max(front[i], z);
            back[i] = MathF.Min(back[i], z);
            half[i] = MathF.Max(half[i], x);
        }
        return new TorsoShape(rig, bone, bottom, front, back, half);
    }

    /// <summary>
    /// How far (in skeleton units) a posed point lies inside the torso toward its front
    /// surface, plus <paramref name="margin"/>; 0 when it is outside.
    /// </summary>
    public float Depth(Vector3 skeletonPoint, float margin)
    {
        // Into the torso's rest frame, so the outline follows bending and twisting.
        var posed = _rig.Skeleton.GetBoneGlobalPose(_bone);
        var local = _restGlobal * (posed.AffineInverse() * skeletonPoint) - _restGlobal.Origin;
        float y = local.Dot(_rig.Up), x = MathF.Abs(local.Dot(_rig.Left)), z = local.Dot(_rig.Forward);
        int i = (int)((y - _bottom) / Slice);
        if (i < 0 || i >= _front.Length || _front[i] == float.MinValue)
            return 0f;
        if (x > _half[i] * 0.92f || z < _back[i] + 0.5f * (_front[i] - _back[i]) - 0.05f)
            return 0f;
        float depth = _front[i] + margin - z;
        return depth > 0f ? depth : 0f;
    }
}
