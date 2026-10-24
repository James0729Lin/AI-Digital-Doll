using System;
using System.Collections.Generic;
using System.Linq;

namespace DigitalDoll.Core.Mascot;

/// <summary>A horizontal surface the mascot can stand on (window pixels, y grows downward).</summary>
public readonly record struct Platform(long Id, float X0, float X1, float Y)
{
    public float Width => X1 - X0;
    public bool Spans(float x) => x >= X0 && x <= X1;
}

/// <summary>
/// A vertical surface the mascot can climb: a screen edge or the side of a window.
/// <see cref="Side"/> is the direction from the mascot to the wall (-1: the wall is on
/// its left). <see cref="TopPlatform"/> is the window top it can climb onto at the
/// upper end, if any.
/// </summary>
public readonly record struct Wall(long Id, float X, float Top, float Bottom, int Side, long? TopPlatform = null)
{
    public bool Spans(float y) => y >= Top - 0.5f && y <= Bottom + 0.5f;
}

/// <summary>A window on the desktop in window-local pixels, listed top of the Z-order first.</summary>
public readonly record struct DesktopWindow(long Id, float Left, float Top, float Right, float Bottom);

/// <summary>Everything the mascot can stand on or climb.</summary>
public sealed class Surroundings
{
    public const long FloorId = 0;
    public const long LeftWallId = -1;
    public const long RightWallId = -2;

    public Surroundings(float width, float height, IReadOnlyList<Platform> platforms, IReadOnlyList<Wall> walls)
    {
        Width = width;
        Height = height;
        Platforms = platforms;
        Walls = walls;
    }

    public float Width { get; }
    public float Height { get; }

    /// <summary>The floor (always <see cref="FloorId"/>) followed by window tops.</summary>
    public IReadOnlyList<Platform> Platforms { get; }
    public IReadOnlyList<Wall> Walls { get; }

    public Platform Floor => Platforms.First(p => p.Id == FloorId);

    public Platform? FindPlatform(long id)
    {
        foreach (var p in Platforms)
        {
            if (p.Id == id)
                return p;
        }
        return null;
    }

    public Wall? FindWall(long id)
    {
        foreach (var w in Walls)
        {
            if (w.Id == id)
                return w;
        }
        return null;
    }

    /// <summary>Just the floor and the screen edges.</summary>
    public static Surroundings Screen(float width, float height, float characterHeight) =>
        FromWindows(width, height, characterHeight, Array.Empty<DesktopWindow>());

    /// <summary>
    /// Builds the surfaces from the visible windows. Only the uncovered parts of each
    /// window's top edge become platforms, and only where the whole character fits
    /// below the top of the screen and above the floor.
    /// </summary>
    public static Surroundings FromWindows(float width, float height, float characterHeight, IReadOnlyList<DesktopWindow> windows)
    {
        float floorY = height - 1f;
        var platforms = new List<Platform> { new(FloorId, 0f, width, floorY) };
        float minWidth = characterHeight * 0.35f;

        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            float y = w.Top;
            if (y < characterHeight * 1.02f || y > floorY - characterHeight * 0.3f)
                continue;

            // Parts of the edge hidden by windows above it in the Z-order.
            var pieces = new List<(float A, float B)> { (Math.Max(0f, w.Left), Math.Min(width, w.Right)) };
            for (int j = 0; j < i; j++)
            {
                var above = windows[j];
                if (above.Top > y + 2f || above.Bottom < y)
                    continue;
                pieces = Subtract(pieces, above.Left, above.Right);
            }

            int index = 0;
            foreach (var (a, b) in pieces)
            {
                if (b - a >= minWidth)
                    platforms.Add(new Platform(w.Id * 16 + index, a, b, y));
                index++;
            }
        }

        float top = characterHeight * 1.05f;
        var walls = new List<Wall>
        {
            new(LeftWallId, 0f, top, floorY, -1),
            new(RightWallId, width, top, floorY, 1),
        };

        // Window sides: climbed from outside the window, up to its top edge.
        float offset = characterHeight * 0.12f;
        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            foreach (var (x, side, slot) in new[] { (w.Left, 1, 8), (w.Right, -1, 12) })
            {
                // Room for the mascot beside the window, inside the screen.
                float stand = x - side * offset;
                if (stand < 0f || stand > width)
                    continue;

                var pieces = new List<(float A, float B)> { (Math.Max(w.Top, top), Math.Min(w.Bottom, floorY)) };
                for (int j = 0; j < i; j++)
                {
                    var above = windows[j];
                    if (above.Left - offset > x || above.Right + offset < x)
                        continue;
                    pieces = Subtract(pieces, above.Top, above.Bottom);
                }

                int index = 0;
                foreach (var (a, b) in pieces)
                {
                    if (b - a >= characterHeight * 0.4f)
                    {
                        long? topPlatform = null;
                        if (MathF.Abs(a - w.Top) < 0.5f)
                        {
                            foreach (var p in platforms)
                            {
                                if (p.Id / 16 == w.Id && MathF.Abs(p.Y - w.Top) < 0.5f && p.X0 - 2f <= x && x <= p.X1 + 2f)
                                    topPlatform = p.Id;
                            }
                        }
                        walls.Add(new Wall(w.Id * 16 + slot + index, x, a, b, side, topPlatform));
                    }
                    index++;
                }
            }
        }
        return new Surroundings(width, height, platforms, walls);
    }

    private static List<(float, float)> Subtract(List<(float A, float B)> pieces, float cutA, float cutB)
    {
        var result = new List<(float, float)>();
        foreach (var (a, b) in pieces)
        {
            if (cutB <= a || cutA >= b)
            {
                result.Add((a, b));
                continue;
            }
            if (cutA > a)
                result.Add((a, cutA));
            if (cutB < b)
                result.Add((cutB, b));
        }
        return result;
    }
}
