using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DigitalDoll.Core;

/// <summary>A pattern drawn over an animal's main colour.</summary>
public enum CoatPattern
{
    None,
    Spots,
    Stripes,
    Patches,
}

/// <summary>What an animal looks like: its colours (as "#rrggbb") and pattern.</summary>
public sealed partial record AnimalCoat
{
    [JsonPropertyName("main")]
    public string Main { get; init; } = "#38383f";

    /// <summary>Chest, belly and paws (where the model has them).</summary>
    [JsonPropertyName("second")]
    public string Second { get; init; } = "#f5f2ed";

    /// <summary>Inside of the ears and the nose (where the model has them).</summary>
    [JsonPropertyName("accent")]
    public string Accent { get; init; } = "#f59ebd";

    [JsonPropertyName("pattern")]
    public CoatPattern Pattern { get; init; } = CoatPattern.None;

    [JsonPropertyName("patternColor")]
    public string PatternColor { get; init; } = "#1e1a18";

    /// <summary>0 small and fine … 1 large.</summary>
    [JsonPropertyName("patternSize")]
    public float PatternSize { get; init; } = 0.5f;

    /// <summary>0 hardly any … 1 covering most of it.</summary>
    [JsonPropertyName("patternAmount")]
    public float PatternAmount { get; init; } = 0.5f;

    public static AnimalCoat Default(Species species) => species switch
    {
        Species.Dog => new AnimalCoat { Main = "#8c8a85" },
        _ => new AnimalCoat(),
    };

    /// <summary>The same coat with anything out of range (a hand-edited file) put right.</summary>
    public AnimalCoat Checked(Species species)
    {
        var fallback = Default(species);
        return this with
        {
            Main = Hex(Main, fallback.Main),
            Second = Hex(Second, fallback.Second),
            Accent = Hex(Accent, fallback.Accent),
            PatternColor = Hex(PatternColor, fallback.PatternColor),
            Pattern = Enum.IsDefined(Pattern) ? Pattern : CoatPattern.None,
            PatternSize = Unit(PatternSize, fallback.PatternSize),
            PatternAmount = Unit(PatternAmount, fallback.PatternAmount),
        };
    }

    private static string Hex(string? value, string fallback) =>
        value != null && HexColor().IsMatch(value) ? value.ToLowerInvariant() : fallback;

    private static float Unit(float value, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : fallback;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}

/// <summary>A ready-made coat to start from.</summary>
public sealed record CoatPreset(string Chinese, string English, AnimalCoat Coat);

public static class CoatPresets
{
    private static readonly CoatPreset[] Cats =
    {
        new("賓士貓", "Tuxedo", new AnimalCoat()),
        new("橘虎斑", "Orange tabby", new AnimalCoat { Main = "#e0913a", Second = "#fbeedd", Accent = "#f2a0a0", Pattern = CoatPattern.Stripes, PatternColor = "#a85a1c", PatternSize = 0.45f, PatternAmount = 0.5f }),
        new("灰虎斑", "Grey tabby", new AnimalCoat { Main = "#8d8f94", Second = "#eeeae3", Accent = "#e3a3a8", Pattern = CoatPattern.Stripes, PatternColor = "#3a3b40", PatternSize = 0.45f, PatternAmount = 0.5f }),
        new("三花貓", "Calico", new AnimalCoat { Main = "#f4efe6", Second = "#fbf8f2", Accent = "#f0a4a8", Pattern = CoatPattern.Patches, PatternColor = "#d9822b", PatternSize = 0.6f, PatternAmount = 0.5f }),
        new("豹紋貓", "Spotted", new AnimalCoat { Main = "#d9b26f", Second = "#f8efdc", Accent = "#e8a08f", Pattern = CoatPattern.Spots, PatternColor = "#3b2815", PatternSize = 0.4f, PatternAmount = 0.6f }),
        new("純白貓", "White", new AnimalCoat { Main = "#f5f2ed", Second = "#fbf9f5", Accent = "#f6a9c0" }),
    };

    private static readonly CoatPreset[] Dogs =
    {
        new("灰狗", "Grey", AnimalCoat.Default(Species.Dog)),
        new("柴犬色", "Tan", new AnimalCoat { Main = "#c98a4b" }),
        new("大麥町", "Dalmatian", new AnimalCoat { Main = "#f2eee7", Pattern = CoatPattern.Spots, PatternColor = "#1c1a19", PatternSize = 0.4f, PatternAmount = 0.6f }),
        new("花斑狗", "Patched", new AnimalCoat { Main = "#f0e9dd", Pattern = CoatPattern.Patches, PatternColor = "#6b4426", PatternSize = 0.65f, PatternAmount = 0.5f }),
        new("黑狗", "Black", new AnimalCoat { Main = "#2a2a2d" }),
    };

    public static IReadOnlyList<CoatPreset> For(Species species) => species == Species.Dog ? Dogs : Cats;
}
