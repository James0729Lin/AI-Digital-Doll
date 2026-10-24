using System;
using System.Collections.Generic;
using System.Numerics;

namespace DigitalDoll.Core.Geometry;

/// <summary>
/// Builds the clickable window region around the character. On Windows the
/// region also clips what is drawn, so it must fully cover the character.
/// </summary>
public static class HitRegion
{
    /// <summary>
    /// Returns a convex polygon (counter-clockwise in a y-down space) that covers every
    /// point grown by <paramref name="margin"/> pixels, clamped to the window size.
    /// </summary>
    public static Vector2[] Build(IReadOnlyList<Vector2> points, float margin, Vector2 windowSize)
    {
        if (points.Count == 0)
            return Array.Empty<Vector2>();

        var grown = new List<Vector2>(points.Count * 4);
        foreach (var p in points)
        {
            grown.Add(Clamp(new Vector2(p.X - margin, p.Y - margin), windowSize));
            grown.Add(Clamp(new Vector2(p.X + margin, p.Y - margin), windowSize));
            grown.Add(Clamp(new Vector2(p.X + margin, p.Y + margin), windowSize));
            grown.Add(Clamp(new Vector2(p.X - margin, p.Y + margin), windowSize));
        }
        return ConvexHull(grown);
    }

    /// <summary>Andrew's monotone chain. Collinear points are dropped.</summary>
    public static Vector2[] ConvexHull(IReadOnlyList<Vector2> input)
    {
        var pts = new List<Vector2>(input);
        pts.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        if (pts.Count < 3)
            return pts.ToArray();

        var hull = new Vector2[pts.Count * 2];
        int k = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            while (k >= 2 && Cross(hull[k - 2], hull[k - 1], pts[i]) <= 0f)
                k--;
            hull[k++] = pts[i];
        }
        for (int i = pts.Count - 2, lower = k + 1; i >= 0; i--)
        {
            while (k >= lower && Cross(hull[k - 2], hull[k - 1], pts[i]) <= 0f)
                k--;
            hull[k++] = pts[i];
        }
        Array.Resize(ref hull, k - 1);
        return hull;
    }

    /// <summary>True when the two polygons differ by more than <paramref name="tolerance"/> pixels.</summary>
    public static bool Differs(Vector2[] a, Vector2[] b, float tolerance)
    {
        if (a.Length != b.Length)
            return true;
        for (int i = 0; i < a.Length; i++)
        {
            if (Vector2.DistanceSquared(a[i], b[i]) > tolerance * tolerance)
                return true;
        }
        return false;
    }

    /// <summary>True when <paramref name="p"/> lies inside or on a convex polygon of either winding.</summary>
    public static bool Contains(Vector2[] convex, Vector2 p)
    {
        if (convex.Length < 3)
            return false;
        int sign = 0;
        for (int i = 0; i < convex.Length; i++)
        {
            float c = Cross(convex[i], convex[(i + 1) % convex.Length], p);
            if (c == 0f)
                continue;
            int s = c > 0f ? 1 : -1;
            if (sign == 0)
                sign = s;
            else if (s != sign)
                return false;
        }
        return true;
    }

    public static bool ContainsAll(Vector2[] convex, IReadOnlyList<Vector2> points)
    {
        foreach (var p in points)
        {
            if (!Contains(convex, p))
                return false;
        }
        return true;
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b) =>
        (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    private static Vector2 Clamp(Vector2 p, Vector2 size) =>
        new(Math.Clamp(p.X, 0f, size.X), Math.Clamp(p.Y, 0f, size.Y));
}
