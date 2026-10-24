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
    public CharacterSource Character { get; set; } = CharacterSource.Default;

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
