using System;
using System.Collections.Generic;
using Godot;

namespace DigitalDoll.Character;

/// <summary>
/// A tight screen outline of the character for the window's mouse region. Every mesh
/// vertex (hair, clothes and accessories included) is assigned to its nearest bone
/// segment; each segment keeps how far the mesh reaches in 26 directions around its
/// ends. At runtime those points follow the bones, are projected, wrapped in a
/// convex hull per segment, and the hulls are merged. Gaps between the legs or
/// between an arm and the body stay click-through.
/// </summary>
public sealed class CharacterHitShape
{
    // A point placed along a direction bin can sit slightly inside vertices that lie
    // between bins; this covers the gap (1 / cos of the widest bin half-angle).
    private const float BinCover = 1.25f;

    private static readonly Vector3[] Directions = BuildDirections();

    private sealed class Segment
    {
        public int Bone;
        public Vector3 A, B;
        public Transform3D RestInverse;
        public readonly float[] ExtentA = new float[Directions.Length];
        public readonly float[] ExtentB = new float[Directions.Length];
        public readonly List<Vector3> RestPoints = new();
    }

    private readonly Skeleton3D _skeleton;
    private readonly List<Segment> _segments = new();

    private CharacterHitShape(Skeleton3D skeleton) => _skeleton = skeleton;

    /// <summary>Measures the meshes around each bone. Call before the character is animated.</summary>
    public static CharacterHitShape Measure(Node model, Skeleton3D skeleton)
    {
        var shape = new CharacterHitShape(skeleton);
        var segments = new List<Segment>();
        foreach (var (from, to) in BoneSegments(skeleton))
        {
            var rest = skeleton.GetBoneGlobalRest(from);
            segments.Add(new Segment
            {
                Bone = from,
                A = rest.Origin,
                B = skeleton.GetBoneGlobalRest(to).Origin,
                RestInverse = rest.AffineInverse(),
            });
        }
        if (segments.Count == 0)
            return shape;
        // Each humanoid bone -> segments it drives; other bones (hair springs, twist
        // bones) count as their nearest humanoid ancestor.
        var humanoid = HumanoidAncestors(skeleton);
        var byBone = new Dictionary<int, List<Segment>>();
        foreach (var s in segments)
        {
            if (!byBone.TryGetValue(s.Bone, out var list))
                byBone[s.Bone] = list = new List<Segment>();
            list.Add(s);
        }

        foreach (var (p, bone) in RestVertices(model, skeleton))
        {
            // Skinned vertices go to the segments of the bone that moves them; only
            // unskinned meshes fall back to the nearest segment.
            IEnumerable<Segment> candidates = segments;
            if (bone >= 0 && humanoid.TryGetValue(bone, out int owner) && byBone.TryGetValue(owner, out var owned))
                candidates = owned;

            Segment? best = null;
            float bestDistance = float.MaxValue, bestT = 0f;
            foreach (var s in candidates)
            {
                var ab = s.B - s.A;
                float lengthSquared = ab.LengthSquared();
                float t = lengthSquared > 1e-10f ? Mathf.Clamp((p - s.A).Dot(ab) / lengthSquared, 0f, 1f) : 0f;
                float d = p.DistanceTo(s.A + ab * t);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = s;
                    bestT = t;
                }
            }
            if (best == null)
                continue;
            var end = bestT < 0.5f ? best.A : best.B;
            var extents = bestT < 0.5f ? best.ExtentA : best.ExtentB;
            var offset = p - end;
            float length = offset.Length();
            if (length < 1e-6f)
                continue;
            int bin = 0;
            float bestDot = float.MinValue;
            for (int k = 0; k < Directions.Length; k++)
            {
                float dot = Directions[k].Dot(offset);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bin = k;
                }
            }
            extents[bin] = MathF.Max(extents[bin], length);
        }

        foreach (var s in segments)
        {
            for (int k = 0; k < Directions.Length; k++)
            {
                if (s.ExtentA[k] > 0f)
                    s.RestPoints.Add(s.A + Directions[k] * s.ExtentA[k] * BinCover);
                if (s.ExtentB[k] > 0f)
                    s.RestPoints.Add(s.B + Directions[k] * s.ExtentB[k] * BinCover);
            }
            if (s.RestPoints.Count > 0)
            {
                s.RestPoints.Add(s.A);
                s.RestPoints.Add(s.B);
                shape._segments.Add(s);
            }
        }
        return shape;
    }

    /// <summary>The merged outline in viewport pixels, grown by <paramref name="margin"/> pixels.</summary>
    public Vector2[] Outline(Camera3D camera, float margin)
    {
        var toWorld = _skeleton.GlobalTransform;
        Vector2[]? merged = null;
        bool split = false;
        var all = new List<Vector2>();
        var projected = new List<Vector2>();

        foreach (var s in _segments)
        {
            // Rest space -> current pose of the segment's bone -> world -> screen.
            var follow = toWorld * _skeleton.GetBoneGlobalPose(s.Bone) * s.RestInverse;
            projected.Clear();
            foreach (var p in s.RestPoints)
                projected.Add(camera.UnprojectPosition(follow * p));
            var hull = Grow(Geometry2D.ConvexHull(projected.ToArray()), margin);
            if (hull.Length < 3)
                continue;
            all.AddRange(hull);
            if (split)
                continue;
            merged = merged == null ? hull : Union(merged, hull);
            if (merged == null)
            {
                if (OS.IsDebugBuild())
                    GD.Print($"hit shape split at {_skeleton.GetBoneName(s.Bone)}");
                split = true;
            }
        }

        // Neighbouring segments overlap at the joints, so the union is normally one
        // piece. If it ever splits, use the convex hull so nothing gets clipped.
        return split || merged == null ? Geometry2D.ConvexHull(all.ToArray()) : merged;
    }

    /// <summary>
    /// Segments between each humanoid bone and its nearest humanoid ancestor, plus a
    /// point segment at every end bone (head, hands, feet). Following the model's own
    /// hierarchy copes with optional bones such as UpperChest.
    /// </summary>
    private static List<(int From, int To)> BoneSegments(Skeleton3D skeleton)
    {
        var humanoid = new HashSet<int>();
        foreach (var name in HumanoidRig.AnimatedBones)
        {
            int index = skeleton.FindBone(name);
            if (index >= 0)
                humanoid.Add(index);
        }

        var segments = new List<(int, int)>();
        var hasChild = new HashSet<int>();
        foreach (int bone in humanoid)
        {
            int parent = skeleton.GetBoneParent(bone);
            while (parent >= 0 && !humanoid.Contains(parent))
                parent = skeleton.GetBoneParent(parent);
            if (parent < 0)
                continue;
            segments.Add((parent, bone));
            hasChild.Add(parent);
        }
        foreach (int bone in humanoid)
        {
            if (!hasChild.Contains(bone))
                segments.Add((bone, bone));
        }
        return segments;
    }

    /// <summary>For every bone, the nearest humanoid bone at or above it that drives a segment.</summary>
    private static Dictionary<int, int> HumanoidAncestors(Skeleton3D skeleton)
    {
        var humanoid = new HashSet<int>();
        foreach (var name in HumanoidRig.AnimatedBones)
        {
            int index = skeleton.FindBone(name);
            if (index >= 0)
                humanoid.Add(index);
        }
        var result = new Dictionary<int, int>();
        for (int bone = 0; bone < skeleton.GetBoneCount(); bone++)
        {
            int b = bone;
            while (b >= 0 && !humanoid.Contains(b))
                b = skeleton.GetBoneParent(b);
            if (b >= 0)
                result[bone] = b;
        }
        return result;
    }

    /// <summary>Rest-pose positions of every mesh vertex in skeleton space, with the bone that moves it most (-1 if unskinned).</summary>
    private static IEnumerable<(Vector3 Position, int Bone)> RestVertices(Node model, Skeleton3D skeleton)
    {
        var toSkeleton = skeleton.GlobalTransform.AffineInverse();
        foreach (var node in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            var instance = (MeshInstance3D)node;
            if (instance.Mesh == null || !instance.Visible)
                continue;
            var skin = instance.Skin;
            Transform3D[]? bindToRest = null;
            int[]? bindBone = null;
            if (skin != null)
            {
                bindToRest = new Transform3D[skin.GetBindCount()];
                bindBone = new int[bindToRest.Length];
                for (int i = 0; i < bindToRest.Length; i++)
                {
                    int bone = skin.GetBindBone(i);
                    if (bone < 0)
                        bone = skeleton.FindBone(skin.GetBindName(i));
                    bindBone[i] = bone;
                    bindToRest[i] = (bone >= 0 ? skeleton.GetBoneGlobalRest(bone) : Transform3D.Identity) * skin.GetBindPose(i);
                }
            }
            var toSpace = toSkeleton * instance.GlobalTransform;

            for (int s = 0; s < instance.Mesh.GetSurfaceCount(); s++)
            {
                var arrays = instance.Mesh.SurfaceGetArrays(s);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int[]? bones = null;
                float[]? weights = null;
                if (bindToRest != null && arrays[(int)Mesh.ArrayType.Bones].VariantType != Variant.Type.Nil)
                {
                    bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                    weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                }
                int influences = bones != null && vertices.Length > 0 ? bones.Length / vertices.Length : 0;

                for (int v = 0; v < vertices.Length; v++)
                {
                    if (influences == 0)
                    {
                        yield return (toSpace * vertices[v], -1);
                        continue;
                    }
                    var sum = Vector3.Zero;
                    float total = 0f, strongest = 0f;
                    int dominant = -1;
                    for (int k = 0; k < influences; k++)
                    {
                        float w = weights![v * influences + k];
                        int bind = bones![v * influences + k];
                        if (w <= 0f || bind >= bindToRest!.Length)
                            continue;
                        sum += (bindToRest[bind] * vertices[v]) * w;
                        total += w;
                        if (w > strongest)
                        {
                            strongest = w;
                            dominant = bindBone![bind];
                        }
                    }
                    yield return total > 0f ? (sum / total, dominant) : (toSpace * vertices[v], -1);
                }
            }
        }
    }

    internal static Vector2[]? Union(Vector2[] a, Vector2[] b)
    {
        Vector2[]? outer = null;
        foreach (var polygon in Geometry2D.MergePolygons(a, b))
        {
            if (Geometry2D.IsPolygonClockwise(polygon))
                continue; // a hole
            if (outer != null)
                return null; // disjoint pieces
            outer = polygon;
        }
        return outer;
    }

    internal static Vector2[] Grow(Vector2[] polygon, float margin)
    {
        if (polygon.Length < 3 || margin <= 0f)
            return polygon;
        var grown = Geometry2D.OffsetPolygon(polygon, margin);
        return grown.Count > 0 ? grown[0] : polygon;
    }

    private static Vector3[] BuildDirections()
    {
        var directions = new List<Vector3>();
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    if (x != 0 || y != 0 || z != 0)
                        directions.Add(new Vector3(x, y, z).Normalized());
                }
            }
        }
        return directions.ToArray();
    }
}
