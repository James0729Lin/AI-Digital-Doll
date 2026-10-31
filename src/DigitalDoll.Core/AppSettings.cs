using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalDoll.Core;

public enum CharacterSource
{
    /// <summary>The bundled sample character.</summary>
    Default,

    /// <summary>A VRM file the user loaded.</summary>
    Vrm,

    /// <summary>The body made in the body editor.</summary>
    Body,

    /// <summary>An animal (see <see cref="AppSettings.Animal"/>).</summary>
    Animal,
}

public enum Species
{
    Cat,
    Dog,
    Chicken,
    Pig,
}

/// <summary>How an animal is drawn: each species can come in several versions.</summary>
public enum AnimalStyle
{
    /// <summary>1.0: built from rounded shapes, with its own procedural motion.</summary>
    Classic,

    /// <summary>2.0: Kenney's "Cube Pets" (CC0), blocky and animated.</summary>
    Cube,

    /// <summary>3.0: Quaternius' low-poly animals (CC0), animated (cat and dog only).</summary>
    LowPoly,
}

/// <summary>User preferences stored as JSON in the user data folder.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [JsonPropertyName("character")]
    public CharacterSource Character { get; set; } = CharacterSource.Animal;

    [JsonPropertyName("animal")]
    public Species Animal { get; set; } = Species.Cat;

    [JsonPropertyName("animalStyle")]
    public AnimalStyle AnimalStyle { get; set; } = AnimalStyle.LowPoly;

    /// <summary>The coats the user gave the animals, by <see cref="CoatKey"/>.</summary>
    [JsonPropertyName("coats")]
    public Dictionary<string, AnimalCoat> Coats { get; set; } = new();

    public static string CoatKey(Species species, AnimalStyle style) => $"{species}.{style}".ToLowerInvariant();

    /// <summary>The animal's coat as the user left it, or as it comes.</summary>
    public AnimalCoat Coat(Species species, AnimalStyle style) =>
        Coats.TryGetValue(CoatKey(species, style), out var coat) && coat != null ? coat.Checked(species) : AnimalCoat.Default(species);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Reads settings; a missing or damaged file gives the defaults.</summary>
    public static AppSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, Json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }
}
