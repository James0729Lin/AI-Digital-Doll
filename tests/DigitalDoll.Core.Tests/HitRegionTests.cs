using System.Linq;
using System.Numerics;
using DigitalDoll.Core.Geometry;
using Xunit;

namespace DigitalDoll.Core.Tests;

public class HitRegionTests
{
    private static readonly Vector2 Window = new(1920f, 1040f);

    [Fact]
    public void HullOfSquareWithInteriorPointHasFourCorners()
    {
        var hull = HitRegion.ConvexHull(new[]
        {
            new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10), new Vector2(5, 5),
        });
        Assert.Equal(4, hull.Length);
        Assert.DoesNotContain(new Vector2(5, 5), hull);
    }

    [Fact]
    public void BuildCoversEveryPointWithMargin()
    {
        var points = new[] { new Vector2(500, 500), new Vector2(520, 700), new Vector2(480, 900) };
        var region = HitRegion.Build(points, 20f, Window);

        Assert.True(region.Length >= 3);
        foreach (var p in points)
        {
            Assert.True(Contains(region, p + new Vector2(19, 19)));
            Assert.True(Contains(region, p - new Vector2(19, 19)));
        }
    }

    [Fact]
    public void BuildIsClampedToTheWindow()
    {
        var region = HitRegion.Build(new[] { new Vector2(5, 1035) }, 30f, Window);
        Assert.All(region, p =>
        {
            Assert.InRange(p.X, 0f, Window.X);
            Assert.InRange(p.Y, 0f, Window.Y);
        });
    }

    [Fact]
    public void EmptyInputGivesEmptyRegion()
    {
        Assert.Empty(HitRegion.Build(System.Array.Empty<Vector2>(), 10f, Window));
    }

    [Fact]
    public void DiffersDetectsMovementBeyondTolerance()
    {
        var a = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10) };
        var nudged = a.Select(p => p + new Vector2(1, 0)).ToArray();
        var moved = a.Select(p => p + new Vector2(5, 0)).ToArray();

        Assert.False(HitRegion.Differs(a, nudged, 2f));
        Assert.True(HitRegion.Differs(a, moved, 2f));
        Assert.True(HitRegion.Differs(a, a.Take(2).ToArray(), 2f));
    }

    [Fact]
    public void ConvexContainsMatchesInsideAndOutside()
    {
        var square = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) };
        Assert.True(HitRegion.Contains(square, new Vector2(5, 5)));
        Assert.True(HitRegion.Contains(square, new Vector2(10, 5)));
        Assert.False(HitRegion.Contains(square, new Vector2(11, 5)));
        Assert.True(HitRegion.Contains(square.Reverse().ToArray(), new Vector2(5, 5)));
        Assert.False(HitRegion.Contains(square.Take(2).ToArray(), new Vector2(5, 0)));
    }

    [Fact]
    public void ContainsAllRequiresEveryPoint()
    {
        var region = HitRegion.Build(new[] { new Vector2(100, 100) }, 50f, Window);
        Assert.True(HitRegion.ContainsAll(region, new[] { new Vector2(90, 90), new Vector2(140, 140) }));
        Assert.False(HitRegion.ContainsAll(region, new[] { new Vector2(90, 90), new Vector2(160, 100) }));
    }

    private static bool Contains(Vector2[] polygon, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}
