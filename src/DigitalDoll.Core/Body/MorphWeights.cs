using System;
using System.Collections.Generic;

namespace DigitalDoll.Core.Body;

/// <summary>
/// Turns slider values into target weights.
///
/// Macro targets come in a grid. "{race}-{gender}-{age}" targets give the base shape
/// for each gender and age. "universal-…-{muscle}-{weight}" targets add muscle and
/// weight on top (their average/average entries are empty). Height, proportions and
/// breast targets add further variations on the same grid. Each axis is interpolated
/// linearly between its key values and the grid weights are multiplied together.
/// Only adult ages (young, old) are available.
/// </summary>
public static class MorphWeights
{
    private static readonly string[] Genders = { "female", "male" };
    private static readonly string[] Ages = { "young", "old" };
    private static readonly string[] Levels = { "min", "average", "max" };
    private static readonly string[] Races = { "african", "asian", "caucasian" };

    public const string FabricSmoothing = "breast/nipple-point-decr";

    /// <summary>
    /// Anime proportions, added on top of the user's own sliders when the anime style
    /// is chosen: big open eyes, small nose and mouth, a narrow chin, a slightly larger
    /// head and a slimmer neck.
    /// </summary>
    public static readonly IReadOnlyList<(string Target, float Weight)> AnimeFace = new[]
    {
        ("eyes/{side}-eye-scale-incr", 1f),
        ("eyes/{side}-eye-height2-incr", 0.7f),
        ("eyes/{side}-eye-trans-down", 0.25f),
        ("eyes/{side}-eye-trans-out", 0.2f),
        ("eyes/{side}-eye-epicanthus-out", 0.3f),
        ("nose/nose-scale-horiz-decr", 0.7f),
        ("nose/nose-scale-vert-decr", 0.45f),
        ("nose/nose-scale-depth-decr", 0.5f),
        ("nose/nose-point-width-decr", 0.7f),
        ("nose/nose-nostrils-width-decr", 0.7f),
        ("mouth/mouth-scale-horiz-decr", 0.4f),
        ("mouth/mouth-upperlip-volume-decr", 0.6f),
        ("mouth/mouth-lowerlip-volume-decr", 0.5f),
        ("chin/chin-width-decr", 0.6f),
        ("chin/chin-bones-decr", 0.7f),
        ("chin/chin-prominent-decr", 0.3f),
        ("cheek/{side}-cheek-bones-decr", 0.6f),
        ("head/head-invertedtriangular", 0.45f),
        ("head/head-scale-horiz-incr", 0.25f),
        ("head/head-scale-vert-incr", 0.2f),
        ("head/head-scale-depth-incr", 0.2f),
        ("forehead/forehead-scale-vert-incr", 0.3f),
        ("neck/neck-scale-horiz-decr", 0.5f),
        ("ears/{side}-ear-scale-decr", 0.3f),
    };

    public static Dictionary<string, float> Compute(BodyShape shape)
    {
        var weights = new Dictionary<string, float>();

        float gender = shape.Get("gender");
        float[] genderW = { 1f - gender, gender };
        float age = shape.Get("age");
        float[] ageW = { 1f - age, age };
        float[] muscleW = Three(shape.Get("muscle"));
        float[] weightW = Three(shape.Get("weight"));
        float[] raceW = Normalised(Races, shape);

        float height = shape.Get("height") * 2f - 1f;
        float proportions = shape.Get("proportions") * 2f - 1f;
        float[] cupW = Three(shape.Get("breast-size"));
        float[] firmW = Three(shape.Get("breast-firmness"));

        for (int g = 0; g < 2; g++)
        {
            for (int a = 0; a < 2; a++)
            {
                float ga = genderW[g] * ageW[a];
                if (ga <= 0f)
                    continue;
                for (int r = 0; r < Races.Length; r++)
                    Add(weights, $"macrodetails/{Races[r]}-{Genders[g]}-{Ages[a]}", ga * raceW[r]);

                for (int m = 0; m < 3; m++)
                {
                    for (int w = 0; w < 3; w++)
                    {
                        float grid = ga * muscleW[m] * weightW[w];
                        if (grid <= 0f)
                            continue;
                        string cell = $"{Genders[g]}-{Ages[a]}-{Levels[m]}muscle-{Levels[w]}weight";
                        Add(weights, "macrodetails/universal-" + cell, grid);

                        if (height < 0f)
                            Add(weights, $"macrodetails/height/{cell}-minheight", grid * -height);
                        else
                            Add(weights, $"macrodetails/height/{cell}-maxheight", grid * height);

                        if (proportions < 0f)
                            Add(weights, $"macrodetails/proportions/{cell}-uncommonproportions", grid * -proportions);
                        else
                            Add(weights, $"macrodetails/proportions/{cell}-idealproportions", grid * proportions);

                        if (g == 0)
                        {
                            for (int c = 0; c < 3; c++)
                            {
                                for (int f = 0; f < 3; f++)
                                {
                                    if (c == 1 && f == 1)
                                        continue; // average cup and firmness is the base shape
                                    Add(weights, $"breast/{cell}-{Levels[c]}cup-{Levels[f]}firmness", grid * cupW[c] * firmW[f]);
                                }
                            }
                        }
                    }
                }
            }
        }

        // The fitted top covers the chest; keep its surface smooth like fabric.
        Add(weights, FabricSmoothing, 1f);

        if (shape.IsAnime)
        {
            foreach (var (target, weight) in AnimeFace)
            {
                if (target.Contains("{side}"))
                {
                    Add(weights, target.Replace("{side}", "l"), weight);
                    Add(weights, target.Replace("{side}", "r"), weight);
                }
                else
                {
                    Add(weights, target, weight);
                }
            }
        }

        foreach (var def in BodyCatalog.Sliders)
        {
            if (def.Kind == SliderKind.Macro)
                continue;
            if (def.Mirrored)
            {
                AddSlider(weights, def, shape.Get(def.Id + ".l"), "l");
                AddSlider(weights, def, shape.Get(def.Id + ".r"), "r");
            }
            else
            {
                AddSlider(weights, def, shape.Get(def.Id), null);
            }
        }
        return weights;
    }

    /// <summary>Weights of the min / average / max key values for a 0..1 parameter.</summary>
    public static float[] Three(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value < 0.5f
            ? new[] { 1f - value * 2f, value * 2f, 0f }
            : new[] { 0f, 2f - value * 2f, value * 2f - 1f };
    }

    private static float[] Normalised(string[] ids, BodyShape shape)
    {
        var values = new float[ids.Length];
        float sum = 0f;
        for (int i = 0; i < ids.Length; i++)
        {
            values[i] = Math.Max(0f, shape.Get(ids[i]));
            sum += values[i];
        }
        for (int i = 0; i < ids.Length; i++)
            values[i] = sum > 0f ? values[i] / sum : 1f / ids.Length;
        return values;
    }

    private static void AddSlider(Dictionary<string, float> weights, SliderDef def, float value, string? side)
    {
        if (value > 0f && def.Positive != null)
            Add(weights, def.TargetName(def.Positive, side), value);
        else if (value < 0f && def.Negative != null)
            Add(weights, def.TargetName(def.Negative, side), -value);
    }

    private static void Add(Dictionary<string, float> weights, string target, float weight)
    {
        if (weight <= 1e-5f)
            return;
        weights[target] = weights.GetValueOrDefault(target) + weight;
    }
}
