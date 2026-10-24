using System;
using System.Numerics;

namespace DigitalDoll.Core.Body;

/// <summary>An ellipsoid on the neutral base mesh (one side; mirrored in x).</summary>
public readonly record struct JiggleRegion(Vector3 Centre, Vector3 Radii);

public static class JiggleRegions
{
    /// <summary>
    /// Where the breasts and buttocks are on the base mesh: the centre and spread of the
    /// vertices that their shape sliders move (left side, weighted by how far they move).
    /// </summary>
    public static (JiggleRegion Bust, JiggleRegion Buttocks) Find(BodyPack pack) =>
        (Region(pack, "breast/breast-trans-up", 2.2f), Region(pack, "buttocks/buttocks-volume-incr", 1.6f));

    private static JiggleRegion Region(BodyPack pack, string target, float spread)
    {
        var t = pack.Targets[target];
        var p = pack.BasePositions;
        double sw = 0;
        var sum = Vector3.Zero;
        for (int i = 0; i < t.Indices.Length; i++)
        {
            int v = t.Indices[i];
            if (p[v * 3] <= 0f)
                continue;
            float w = new Vector3(t.Deltas[i * 3], t.Deltas[i * 3 + 1], t.Deltas[i * 3 + 2]).Length();
            sw += w;
            sum += w * new Vector3(p[v * 3], p[v * 3 + 1], p[v * 3 + 2]);
        }
        var centre = sum / (float)sw;
        var variance = Vector3.Zero;
        for (int i = 0; i < t.Indices.Length; i++)
        {
            int v = t.Indices[i];
            if (p[v * 3] <= 0f)
                continue;
            float w = new Vector3(t.Deltas[i * 3], t.Deltas[i * 3 + 1], t.Deltas[i * 3 + 2]).Length();
            var d = new Vector3(p[v * 3], p[v * 3 + 1], p[v * 3 + 2]) - centre;
            variance += w * d * d;
        }
        var sd = new Vector3(MathF.Sqrt(variance.X / (float)sw), MathF.Sqrt(variance.Y / (float)sw), MathF.Sqrt(variance.Z / (float)sw));
        return new JiggleRegion(centre, sd * spread);
    }

    /// <summary>
    /// How much the bust moves (0..1): more for bigger and softer breasts, almost
    /// nothing for a flat chest.
    /// </summary>
    public static float BustAmount(BodyShape shape)
    {
        float size = shape.Get("breast-size");
        float firmness = shape.Get("breast-firmness");
        float female = 1f - shape.Get("gender");
        return Math.Clamp(female * (0.15f + 0.85f * size) * (1.25f - 0.6f * firmness), 0f, 1f);
    }

    /// <summary>How much the buttocks move (0.2..1): fuller and heavier move more.</summary>
    public static float ButtocksAmount(BodyShape shape) =>
        Math.Clamp(0.5f + 0.35f * shape.Get("buttocks") + 0.3f * (shape.Get("weight") - 0.5f), 0.2f, 1f);
}

/// <summary>
/// A damped spring that lags behind its anchor: fed the anchor's acceleration, its
/// offset bounces opposite to it and settles back. Used for soft-tissue jiggle.
/// </summary>
public sealed class JiggleSpring
{
    private readonly float _omega;
    private readonly float _damping;
    private readonly float _limit;
    private Vector3 _velocity;

    /// <param name="frequency">Oscillations per second.</param>
    /// <param name="damping">Damping ratio (1 = no overshoot).</param>
    /// <param name="limit">Largest offset, in the anchor's units.</param>
    public JiggleSpring(float frequency, float damping, float limit)
    {
        _omega = 2f * MathF.PI * frequency;
        _damping = damping;
        _limit = limit;
    }

    public Vector3 Offset { get; private set; }

    public void Update(float dt, Vector3 acceleration)
    {
        // Small fixed steps keep a stiff spring stable at any frame rate.
        int steps = Math.Clamp((int)MathF.Ceiling(dt / (1f / 240f)), 1, 16);
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            var force = -_omega * _omega * Offset - 2f * _damping * _omega * _velocity - acceleration;
            _velocity += force * h;
            Offset += _velocity * h;
            float length = Offset.Length();
            if (length > _limit)
            {
                // Hitting the limit absorbs the motion along it.
                var n = Offset / length;
                Offset = n * _limit;
                _velocity -= n * MathF.Max(0f, Vector3.Dot(_velocity, n));
            }
        }
    }

    public void Reset()
    {
        Offset = Vector3.Zero;
        _velocity = Vector3.Zero;
    }
}
