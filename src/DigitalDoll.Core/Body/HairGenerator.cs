using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DigitalDoll.Core.Body;

/// <summary>Hair geometry ready for a mesh (counter-clockwise front faces, like the body).</summary>
public sealed class HairMesh
{
    public List<Vector3> Positions { get; } = new();
    public List<Vector3> Normals { get; } = new();

    /// <summary>x across the lock (0..1), y from root (0) to tip (1).</summary>
    public List<Vector2> Uvs { get; } = new();

    /// <summary>Weight of the head bone; the rest goes to the upper chest so long hair follows the back.</summary>
    public List<float> HeadWeights { get; } = new();

    public List<int> Triangles { get; } = new();

    public bool IsEmpty => Triangles.Count == 0;
}

/// <summary>
/// Grows procedural hair on a morphed body: a scalp layer offset from the real head
/// surface, plus locks that start on the head and fall under gravity while being
/// pushed out of the head, neck, shoulders and torso. Everything is measured on the
/// body it is grown on, so any head or body shape is covered.
/// </summary>
public static class HairGenerator
{
    private sealed class Frame
    {
        public Vector3 Centre;
        public Vector3 Radii;
        public float EyeY, ChinY, NeckY, ShoulderY, ChestY, BackY, Scale;
        public Vector3 TorsoCentre;
        public Vector2 TorsoRadii;
        public float TorsoTop, TorsoBottom;
        public Vector3 NeckCentre;
        public float NeckRadius;
        public Vector3 LeftShoulder, RightShoulder;
        public float ShoulderRadius;
    }

    private readonly record struct Lock(
        float Theta,
        float Elevation,
        float EndY,
        float Width,
        float Thickness,
        Vector3 Push,
        float Gravity = 1f,
        float Layer = 1.04f,
        float Curl = 0f,
        float Taper = 0.8f,
        float MaxLength = 1.2f);

    /// <summary>Scalp test on the neutral base mesh, in head-normalised coordinates.</summary>
    public static bool IsScalp(Vector3 rest, Vector3 centre, Vector3 radii)
    {
        var q = (rest - centre) / radii;
        if (MathF.Abs(q.X) > 1.02f)
            return false; // ears stick out beyond the skull
        float c = q.Z / MathF.Max(MathF.Sqrt(q.X * q.X + q.Z * q.Z), 1e-5f);
        float line = c > 0f ? Lerp(HairlineSide, HairlineFront, c) : Lerp(HairlineSide, HairlineBack, -c);
        return q.Y > line;
    }

    public const float HairlineFront = 0.47f;
    public const float HairlineSide = 0.22f;
    public const float HairlineBack = -0.72f;

    /// <summary>Skull ellipsoid (centre, radii) measured on the given positions.</summary>
    public static (Vector3 Centre, Vector3 Radii) Skull(BodyPack pack, float[] positions)
    {
        var frame = Measure(pack, positions);
        return (frame.Centre, frame.Radii);
    }

    public static HairMesh Generate(BodyPack pack, MorphEngine engine, float[] positions, float[] normals, BodyShape shape)
    {
        var mesh = new HairMesh();
        var style = HairCatalog.ById[shape.Hair];
        if (!style.HasCap)
            return mesh;

        var frame = Measure(pack, positions);
        var rest = Measure(pack, pack.BasePositions);
        AddCap(mesh, pack, positions, normals, rest, frame, style);

        foreach (var spec in Locks(style, shape.Bangs, frame))
            AddLock(mesh, frame, spec);
        return mesh;
    }

    // ---- Measuring the body ---------------------------------------------------

    private static Frame Measure(BodyPack pack, float[] p)
    {
        int Bone(string name) => pack.Bones.Select((b, i) => (b, i)).First(t => t.b.Name == name).i;
        int head = Bone("Head"), neck = Bone("Neck");
        var spineBones = new HashSet<int> { Bone("Spine"), Bone("Chest"), Bone("UpperChest") };
        Vector3 P(int v) => new(p[v * 3], p[v * 3 + 1], p[v * 3 + 2]);
        Vector3 Group(string g) => pack.Groups[g].Aggregate(Vector3.Zero, (s, v) => s + P(v)) / pack.Groups[g].Length;
        Vector3 BoneHead(int b) => pack.Bones[b].HeadVertices.Aggregate(Vector3.Zero, (s, v) => s + P(v)) / pack.Bones[b].HeadVertices.Length;

        var body = pack.Surfaces["body"].Distinct().ToArray();
        int Dominant(int v) => pack.BoneIndices[v * 4];

        var f = new Frame();
        f.EyeY = (Group("l-eye").Y + Group("r-eye").Y) / 2f;
        var headVerts = body.Where(v => Dominant(v) == head).Select(P).ToArray();
        float top = headVerts.Max(v => v.Y);
        var upper = headVerts.Where(v => v.Y > f.EyeY + 0.035f).ToArray();
        float minZ = upper.Min(v => v.Z), maxZ = upper.Max(v => v.Z);
        float rx = upper.Max(v => MathF.Abs(v.X));
        float cy = f.EyeY + 0.01f;
        f.Centre = new Vector3(0f, cy, (minZ + maxZ) / 2f);
        f.Radii = new Vector3(rx, top - cy, (maxZ - minZ) / 2f);
        f.Scale = f.Radii.Y / 0.111f;
        f.ChinY = headVerts.Min(v => v.Y);
        f.NeckY = BoneHead(neck).Y;

        var neckVerts = body.Where(v => Dominant(v) == neck).Select(P).ToArray();
        f.NeckCentre = new Vector3(0f, 0f, neckVerts.Average(v => v.Z));
        f.NeckRadius = neckVerts.Max(v => MathF.Sqrt(v.X * v.X + (v.Z - f.NeckCentre.Z) * (v.Z - f.NeckCentre.Z))) * 0.95f;

        var torso = body.Where(v => spineBones.Contains(Dominant(v))).Select(P).ToArray();
        float tMinZ = torso.Min(v => v.Z), tMaxZ = torso.Max(v => v.Z);
        f.TorsoCentre = new Vector3(0f, 0f, (tMinZ + tMaxZ) / 2f);
        f.TorsoRadii = new Vector2(torso.Max(v => MathF.Abs(v.X)), (tMaxZ - tMinZ) / 2f);
        f.TorsoBottom = BoneHead(Bone("Hips")).Y;
        f.LeftShoulder = BoneHead(Bone("LeftUpperArm"));
        f.RightShoulder = BoneHead(Bone("RightUpperArm"));
        f.TorsoTop = f.LeftShoulder.Y;
        f.ShoulderY = f.LeftShoulder.Y + 0.03f * f.Scale;
        f.ShoulderRadius = 0.06f * f.Scale;
        f.ChestY = BoneHead(Bone("UpperChest")).Y;
        f.BackY = (BoneHead(Bone("Chest")).Y + BoneHead(Bone("Spine")).Y) / 2f;
        return f;
    }

    // ---- Scalp layer ----------------------------------------------------------

    private static void AddCap(HairMesh mesh, BodyPack pack, float[] p, float[] n, Frame rest, Frame frame, HairStyle style)
    {
        var restP = pack.BasePositions;
        bool Scalp(int v) => IsScalp(new Vector3(restP[v * 3], restP[v * 3 + 1], restP[v * 3 + 2]), rest.Centre, rest.Radii);

        float offset = (0.004f + 0.006f * style.Volume) * frame.Scale;
        var map = new Dictionary<int, int>();
        var tris = pack.Surfaces["body"];
        for (int t = 0; t < tris.Length; t += 3)
        {
            if (!Scalp(tris[t]) || !Scalp(tris[t + 1]) || !Scalp(tris[t + 2]))
                continue;
            for (int k = 0; k < 3; k++)
            {
                int v = tris[t + k];
                if (!map.TryGetValue(v, out int local))
                {
                    local = mesh.Positions.Count;
                    map[v] = local;
                    var normal = new Vector3(n[v * 3], n[v * 3 + 1], n[v * 3 + 2]);
                    var position = new Vector3(p[v * 3], p[v * 3 + 1], p[v * 3 + 2]);
                    // Thinner towards the hairline so the edge blends into the painted scalp.
                    var q = (position - frame.Centre) / frame.Radii;
                    float rise = Math.Clamp((q.Y - HairlineSide + 0.25f) / 0.6f, 0.15f, 1f);
                    mesh.Positions.Add(position + normal * offset * rise);
                    mesh.Normals.Add(normal);
                    mesh.Uvs.Add(new Vector2(0.5f, 0f));
                    mesh.HeadWeights.Add(1f);
                }
                mesh.Triangles.Add(local);
            }
        }
    }

    // ---- Locks ----------------------------------------------------------------

    private static IEnumerable<Lock> Locks(HairStyle style, string bangs, Frame f)
    {
        float s = f.Scale;
        float earY = f.EyeY - 0.04f * s;
        float EndY(HairEnd end) => end switch
        {
            HairEnd.Ears => earY,
            HairEnd.Chin => f.ChinY + 0.005f * s,
            HairEnd.Shoulders => f.ShoulderY - 0.02f * s,
            HairEnd.UpperBack => f.ChestY - 0.02f * s,
            _ => f.BackY,
        };

        if (style.Length is HairEnd length && style.Id != "buzz")
        {
            float end = EndY(length);
            float width = (length == HairEnd.Ears ? 0.042f : 0.05f) * s;
            float thickness = 0.012f * s * style.Volume;
            float curl = length is HairEnd.Chin ? 0.6f : length == HairEnd.Shoulders ? 0.35f : 0.15f;
            bool tied = style.Ponytail || style.Twintails;
            var rows = tied ? new[] { 0.45f, 0.15f } : new[] { 0.62f, 0.32f, 0.05f };
            for (int r = 0; r < rows.Length; r++)
            {
                float e = rows[r];
                float spacing = 0.26f + 0.04f * r;
                // Start behind the temples so side hair never covers the eyes.
                for (float theta = 0.98f + (r % 2) * spacing / 2f; theta <= MathF.PI + 0.01f; theta += spacing)
                {
                    foreach (float side in theta >= MathF.PI - 0.01f ? new[] { 1f } : new[] { 1f, -1f })
                    {
                        float th = theta * side;
                        // Back locks go further down than the ones beside the face.
                        float back = MathF.Max(0f, -MathF.Cos(th));
                        float lockEnd = length == HairEnd.MidBack && back < 0.35f ? EndY(HairEnd.UpperBack) : end;
                        if (tied)
                            lockEnd = MathF.Max(lockEnd, earY + 0.01f * s);
                        yield return new Lock(th, e, lockEnd, width, thickness,
                            Push: new Vector3(0f, -1f, 0f), Gravity: 1f, Layer: 1.03f + 0.012f * r * style.Volume,
                            Curl: curl, Taper: 0.75f);
                    }
                }
            }
            if (style.FrontLocks)
            {
                foreach (float side in new[] { 1f, -1f })
                {
                    yield return new Lock(0.92f * side, 0.2f, f.ChestY - 0.08f * s, 0.045f * s, 0.012f * s,
                        Push: new Vector3(0f, -1f, 0.25f), Layer: 1.05f, Curl: 0.1f, Taper: 0.85f);
                }
            }
        }

        if (style.Ponytail)
        {
            for (int i = 0; i < 9; i++)
            {
                float a = i / 8f * MathF.Tau;
                yield return new Lock(MathF.PI + 0.12f * MathF.Cos(a), 0.42f + 0.08f * MathF.Sin(a), f.ChestY - 0.04f * s,
                    0.04f * s, 0.016f * s, Push: new Vector3(0f, 0.7f, -1f), Gravity: 1.4f, Layer: 1.05f, Taper: 0.9f);
            }
        }

        if (style.Twintails)
        {
            foreach (float side in new[] { 1f, -1f })
            {
                for (int i = 0; i < 7; i++)
                {
                    float a = i / 7f * MathF.Tau;
                    yield return new Lock(side * (2.0f + 0.12f * MathF.Cos(a)), 0.4f + 0.08f * MathF.Sin(a), f.ChestY - 0.12f * s,
                        0.038f * s, 0.016f * s, Push: new Vector3(side * 1f, 0.5f, -0.4f), Gravity: 1.4f, Layer: 1.05f,
                        Curl: -0.1f, Taper: 0.9f);
                }
            }
        }

        if (style.Id == "buzz")
            yield break;

        float brow = f.EyeY + 0.02f * s;
        switch (bangs)
        {
            case "straight":
                for (float th = -0.78f; th <= 0.781f; th += 0.105f)
                {
                    float edge = MathF.Abs(th) / 0.78f;
                    yield return new Lock(th, 0.78f, brow + 0.004f * s - edge * edge * 0.02f * s, 0.034f * s, 0.009f * s,
                        Push: new Vector3(0f, -0.4f, 1f), Gravity: 1.2f, Layer: 1.06f, Curl: 0.35f, Taper: 0.45f);
                }
                break;
            case "side":
                for (float th = -0.7f; th <= 0.8f; th += 0.11f)
                {
                    float sweep = (th + 0.7f) / 1.5f;
                    yield return new Lock(th, 0.8f, brow - sweep * 0.035f * s, 0.036f * s, 0.009f * s,
                        Push: new Vector3(-0.45f, -0.6f, 0.6f), Gravity: 1f, Layer: 1.06f, Curl: 0.25f, Taper: 0.6f);
                }
                break;
            case "parted":
                foreach (float side in new[] { 1f, -1f })
                {
                    for (float t = 0.1f; t <= 0.75f; t += 0.11f)
                    {
                        yield return new Lock(t * side, 0.82f, f.EyeY - 0.025f * s, 0.036f * s, 0.009f * s,
                            Push: new Vector3(side * 0.9f, -0.2f, 0.8f), Gravity: 1f, Layer: 1.06f, Curl: 0.3f, Taper: 0.6f);
                    }
                }
                break;
        }
    }

    private static void AddLock(HairMesh mesh, Frame f, Lock spec)
    {
        const int Steps = 14;
        const int Sides = 6;

        var dirOnHead = new Vector3(
            MathF.Sin(spec.Theta) * MathF.Cos(spec.Elevation),
            MathF.Sin(spec.Elevation),
            MathF.Cos(spec.Theta) * MathF.Cos(spec.Elevation));
        var root = f.Centre + dirOnHead * f.Radii * (spec.Layer - 0.02f);
        float step = 0.03f * f.Scale;
        float maxSteps = spec.MaxLength / step;

        // Grow the centre line: initial push, gravity, and collisions with the body.
        var points = new List<Vector3> { root };
        var dir = Vector3.Normalize(dirOnHead * 0.6f + spec.Push);
        var p = root;
        for (int i = 0; i < maxSteps && p.Y > spec.EndY; i++)
        {
            dir = Vector3.Normalize(dir + new Vector3(0f, -0.35f * spec.Gravity, 0f));
            var next = PushOut(p + dir * step, f, spec.Layer);
            if (next.Y < spec.EndY)
            {
                float t = (p.Y - spec.EndY) / MathF.Max(p.Y - next.Y, 1e-5f);
                next = p + (next - p) * Math.Clamp(t, 0f, 1f);
            }
            if (Vector3.DistanceSquared(next, p) < 1e-10f)
                break;
            dir = Vector3.Normalize(next - p);
            points.Add(next);
            p = next;
        }
        if (points.Count < 2)
            return;

        // Resample to a fixed number of rings, curling the tip toward the head axis.
        var rings = Resample(points, Steps);
        for (int i = 0; i < rings.Count; i++)
        {
            float t = i / (float)(rings.Count - 1);
            var axis = new Vector3(f.Centre.X, rings[i].Y, f.Centre.Z);
            var inward = rings[i] - axis;
            inward.Y = 0f;
            if (inward.LengthSquared() > 1e-8f)
                rings[i] -= Vector3.Normalize(inward) * spec.Curl * 0.012f * f.Scale * t * t;
        }

        int baseIndex = mesh.Positions.Count;
        for (int i = 0; i < rings.Count; i++)
        {
            float t = i / (float)(rings.Count - 1);
            var tangent = i < rings.Count - 1 ? rings[i + 1] - rings[i] : rings[i] - rings[i - 1];
            tangent = Vector3.Normalize(tangent);
            var radial = rings[i] - new Vector3(f.Centre.X, rings[i].Y, f.Centre.Z);
            radial = radial.LengthSquared() > 1e-8f ? Vector3.Normalize(radial) : Vector3.UnitZ;
            var across = Vector3.Cross(tangent, radial);
            across = across.LengthSquared() > 1e-8f ? Vector3.Normalize(across) : Vector3.UnitX;
            var outward = Vector3.Normalize(Vector3.Cross(across, tangent));

            float taper = 1f - spec.Taper * MathF.Pow(t, 1.6f);
            float halfWidth = spec.Width * 0.5f * taper;
            float halfThick = spec.Thickness * 0.5f * taper;
            float headWeight = Math.Clamp((rings[i].Y - (f.NeckY - 0.02f * f.Scale)) / (0.08f * f.Scale), 0f, 1f);
            for (int k = 0; k < Sides; k++)
            {
                float a = k / (float)Sides * MathF.Tau;
                var offset = across * MathF.Cos(a) * halfWidth + outward * MathF.Sin(a) * halfThick;
                mesh.Positions.Add(rings[i] + offset);
                var normal = across * MathF.Cos(a) * halfThick + outward * MathF.Sin(a) * halfWidth;
                mesh.Normals.Add(Vector3.Normalize(normal));
                mesh.Uvs.Add(new Vector2(k / (float)Sides, t));
                mesh.HeadWeights.Add(headWeight);
            }
        }
        for (int i = 0; i < rings.Count - 1; i++)
        {
            for (int k = 0; k < Sides; k++)
            {
                int a = baseIndex + i * Sides + k;
                int b = baseIndex + i * Sides + (k + 1) % Sides;
                int c = a + Sides;
                int d = b + Sides;
                mesh.Triangles.AddRange(new[] { a, c, b, b, c, d });
            }
        }
        // Close the tip.
        int tip = mesh.Positions.Count;
        mesh.Positions.Add(rings[^1]);
        mesh.Normals.Add(Vector3.Normalize(rings[^1] - rings[^2]));
        mesh.Uvs.Add(new Vector2(0.5f, 1f));
        mesh.HeadWeights.Add(mesh.HeadWeights[^1]);
        int last = baseIndex + (rings.Count - 1) * Sides;
        for (int k = 0; k < Sides; k++)
            mesh.Triangles.AddRange(new[] { last + k, tip, last + (k + 1) % Sides });
    }

    private static Vector3 PushOut(Vector3 p, Frame f, float layer)
    {
        // Skull.
        var r = f.Radii * layer;
        var q = (p - f.Centre) / r;
        float len = q.Length();
        if (len < 1f && len > 1e-5f)
            p = f.Centre + q / len * r;

        // Neck.
        if (p.Y < f.ChinY + 0.02f * f.Scale && p.Y > f.TorsoTop - 0.02f * f.Scale)
            p = OutOfCircle(p, f.NeckCentre, f.NeckRadius + 0.012f * f.Scale);

        // Shoulders.
        foreach (var shoulder in new[] { f.LeftShoulder, f.RightShoulder })
        {
            var d = p - shoulder;
            float radius = f.ShoulderRadius + 0.012f * f.Scale;
            if (d.Length() < radius && d.Length() > 1e-5f)
                p = shoulder + Vector3.Normalize(d) * radius;
        }

        // Torso (vertical elliptic cylinder).
        if (p.Y < f.TorsoTop && p.Y > f.TorsoBottom)
        {
            float ax = f.TorsoRadii.X * 0.92f + 0.012f * f.Scale, az = f.TorsoRadii.Y + 0.012f * f.Scale;
            float x = p.X / ax, z = (p.Z - f.TorsoCentre.Z) / az;
            float e = MathF.Sqrt(x * x + z * z);
            if (e < 1f && e > 1e-5f)
                p = new Vector3(p.X / e, p.Y, f.TorsoCentre.Z + (p.Z - f.TorsoCentre.Z) / e);
        }
        return p;
    }

    private static Vector3 OutOfCircle(Vector3 p, Vector3 centre, float radius)
    {
        var d = new Vector2(p.X - centre.X, p.Z - centre.Z);
        float len = d.Length();
        if (len >= radius || len < 1e-5f)
            return p;
        d = d / len * radius;
        return new Vector3(centre.X + d.X, p.Y, centre.Z + d.Y);
    }

    private static List<Vector3> Resample(List<Vector3> points, int count)
    {
        var lengths = new float[points.Count];
        for (int i = 1; i < points.Count; i++)
            lengths[i] = lengths[i - 1] + Vector3.Distance(points[i], points[i - 1]);
        float total = lengths[^1];
        var result = new List<Vector3>(count);
        int j = 1;
        for (int i = 0; i < count; i++)
        {
            float target = total * i / (count - 1);
            while (j < points.Count - 1 && lengths[j] < target)
                j++;
            float span = MathF.Max(lengths[j] - lengths[j - 1], 1e-6f);
            float t = Math.Clamp((target - lengths[j - 1]) / span, 0f, 1f);
            result.Add(Vector3.Lerp(points[j - 1], points[j], t));
        }
        return result;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
