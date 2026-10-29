using System;
using Godot;

namespace DigitalDoll.Mascot;

/// <summary>
/// The two round handles shown beside the character while the mouse is near: move (put
/// her down anywhere) and rotate (spin her round). Drawing only; the mascot window does
/// the hit testing and dragging.
/// </summary>
public partial class CharacterHandles : Control
{
    private static readonly Color Fill = new(0.13f, 0.14f, 0.18f, 0.78f);
    private static readonly Color HotFill = new(0.3f, 0.45f, 0.85f, 0.9f);
    private static readonly Color Ink = new(1f, 1f, 1f, 0.95f);

    public Vector2 MoveCentre { get; set; }
    public Vector2 RotateCentre { get; set; }
    public float Radius { get; set; } = 16f;

    /// <summary>0 hidden … 1 fully shown.</summary>
    public float Shown { get; set; }

    /// <summary>The handle under the cursor or being held: 1 move, 2 rotate, 0 none.</summary>
    public int Hot { get; set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Draw()
    {
        if (Shown <= 0.01f)
            return;
        DrawHandle(MoveCentre, Hot == 1, DrawMoveIcon);
        DrawHandle(RotateCentre, Hot == 2, DrawRotateIcon);
    }

    private void DrawHandle(Vector2 centre, bool hot, Action<Vector2, float, Color> icon)
    {
        var fill = hot ? HotFill : Fill;
        fill.A *= Shown;
        var ink = Ink;
        ink.A *= Shown;
        DrawCircle(centre, Radius, fill);
        DrawArc(centre, Radius, 0f, Mathf.Tau, 32, ink with { A = ink.A * 0.5f }, 1.5f, true);
        icon(centre, Radius * 0.55f, ink);
    }

    /// <summary>Four arrows pointing out.</summary>
    private void DrawMoveIcon(Vector2 c, float r, Color ink)
    {
        float w = MathF.Max(1.5f, Radius * 0.12f);
        foreach (var d in new[] { Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right })
        {
            var tip = c + d * r;
            DrawLine(c, tip, ink, w, true);
            var side = new Vector2(-d.Y, d.X) * r * 0.32f;
            DrawColoredPolygon(new[] { tip + d * r * 0.18f, tip - d * r * 0.28f + side, tip - d * r * 0.28f - side }, ink);
        }
    }

    /// <summary>A ring with an arrowhead.</summary>
    private void DrawRotateIcon(Vector2 c, float r, Color ink)
    {
        float w = MathF.Max(1.5f, Radius * 0.12f);
        float start = -Mathf.Pi * 0.35f, end = Mathf.Pi * 1.35f;
        DrawArc(c, r * 0.85f, start, end, 24, ink, w, true);
        var tip = c + new Vector2(MathF.Cos(end), MathF.Sin(end)) * r * 0.85f;
        var along = new Vector2(-MathF.Sin(end), MathF.Cos(end));
        var outward = new Vector2(MathF.Cos(end), MathF.Sin(end));
        DrawColoredPolygon(new[] { tip + along * r * 0.45f, tip + outward * r * 0.35f, tip - outward * r * 0.35f }, ink);
    }
}
