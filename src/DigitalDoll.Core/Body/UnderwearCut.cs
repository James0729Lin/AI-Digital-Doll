using System;
using System.Linq;

namespace DigitalDoll.Core.Body;

/// <summary>
/// Where the fitted base underwear (bandeau top and shorts) sits, in rest-pose
/// coordinates of the neutral base mesh (metres). The body shader compares each
/// pixel's rest position against these limits, so the edges are exact straight
/// cuts and the underwear follows every morph.
/// </summary>
public sealed record UnderwearCut(
    float TopLow,
    float TopHigh,
    float TopHalfWidth,
    float ShortsLow,
    float ShortsHigh,
    float ShortsHalfWidth)
{
    private const string BreastTarget = "breast/female-young-averagemuscle-averageweight-maxcup-averagefirmness";

    public static UnderwearCut Compute(BodyPack pack)
    {
        var p = pack.BasePositions;
        float Y(int v) => p[v * 3 + 1];
        float AbsX(int v) => MathF.Abs(p[v * 3]);

        (float X, float Y) Joint(string name)
        {
            var group = pack.Groups[name];
            return (group.Average(v => p[v * 3]), group.Average(v => p[v * 3 + 1]));
        }

        var hip = Joint("l-upper-leg");
        var shoulder = Joint("l-shoulder");
        var knee = Joint("l-knee");

        // Core breast tissue: where the large-cup macro target moves the surface most.
        var breast = pack.Targets[BreastTarget];
        var lengths = Enumerable.Range(0, breast.Indices.Length).Select(k =>
            MathF.Sqrt(breast.Deltas[k * 3] * breast.Deltas[k * 3]
                + breast.Deltas[k * 3 + 1] * breast.Deltas[k * 3 + 1]
                + breast.Deltas[k * 3 + 2] * breast.Deltas[k * 3 + 2])).ToArray();
        float threshold = lengths.Max() * 0.15f;
        var core = breast.Indices.Where((_, k) => lengths[k] > threshold).ToArray();
        float breastLow = core.Min(Y), breastHigh = core.Max(Y);

        // Lowest point of the centre line between hips and knees. Only body vertices
        // count: helper geometry (the skirt) also lies on the centre line.
        float crotch = pack.Surfaces["body"].Distinct()
            .Where(v => AbsX(v) < 0.001f && Y(v) > knee.Y && Y(v) < hip.Y)
            .Min(Y);

        return new UnderwearCut(
            TopLow: breastLow - 0.015f,
            TopHigh: breastHigh + 0.008f,
            TopHalfWidth: MathF.Abs(shoulder.X) - 0.015f,
            ShortsLow: crotch - 0.025f,
            ShortsHigh: hip.Y + 0.05f,
            // Thighs end well inside this; the hands hang further out.
            ShortsHalfWidth: MathF.Abs(hip.X) + 0.13f);
    }

    /// <summary>The top is worn by female and androgynous bodies; male bodies wear the shorts only.</summary>
    public static bool WearsTop(BodyShape shape) => shape.Get("gender") < 0.75f;

    /// <summary>True when a rest-pose point is covered (same rule as the shader).</summary>
    public bool Covers(float restX, float restY, bool wearsTop = true)
    {
        float x = MathF.Abs(restX);
        bool top = wearsTop && restY > TopLow && restY < TopHigh && x < TopHalfWidth;
        bool shorts = restY > ShortsLow && restY < ShortsHigh && x < ShortsHalfWidth;
        return top || shorts;
    }
}
