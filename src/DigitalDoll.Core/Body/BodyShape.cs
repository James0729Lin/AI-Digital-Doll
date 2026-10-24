using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalDoll.Core.Body;

public static class BodyStyle
{
    public const string Realistic = "realistic";
    public const string Anime = "anime";
}

/// <summary>
/// The editable character: one value per slider key (mirrored sliders have ".l" and
/// ".r" keys) plus the look (art style, colours, hair). Slider values that equal the
/// default are not stored.
/// </summary>
public sealed class BodyShape
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Dictionary<string, float> _values = new();
    // Anime is the app's look (D1 in docs/SPEC.md); realistic remains readable for old files.
    private string _style = BodyStyle.Anime;
    private string _hair = HairCatalog.Default;
    private string _bangs = HairCatalog.DefaultBangs;

    /// <summary><see cref="BodyStyle.Realistic"/> or <see cref="BodyStyle.Anime"/>.</summary>
    public string Style
    {
        get => _style;
        set => _style = value == BodyStyle.Realistic ? BodyStyle.Realistic : BodyStyle.Anime;
    }

    public string SkinColor { get; set; } = "#E0AC8A";
    public string UnderwearColor { get; set; } = "#3A3F4B";
    public string EyeColor { get; set; } = "#5A3A22";
    public string HairColor { get; set; } = "#2B1E18";

    /// <summary>Hair style id from <see cref="HairCatalog.Styles"/>.</summary>
    public string Hair
    {
        get => _hair;
        set => _hair = HairCatalog.ById.ContainsKey(value) ? value : HairCatalog.Default;
    }

    /// <summary>Fringe id from <see cref="HairCatalog.Bangs"/>.</summary>
    public string Bangs
    {
        get => _bangs;
        set => _bangs = HairCatalog.BangsById.ContainsKey(value) ? value : HairCatalog.DefaultBangs;
    }

    public bool IsAnime => _style == BodyStyle.Anime;

    public IReadOnlyDictionary<string, float> Values => _values;

    public float Get(string key)
    {
        if (_values.TryGetValue(key, out float value))
            return value;
        return BodyCatalog.ById.TryGetValue(BaseId(key), out var def) ? def.Default : 0f;
    }

    public void Set(string key, float value)
    {
        if (!BodyCatalog.ById.TryGetValue(BaseId(key), out var def))
            throw new ArgumentException($"Unknown body slider {key}", nameof(key));
        value = Math.Clamp(value, def.Min, def.Max);
        if (MathF.Abs(value - def.Default) < 1e-4f)
            _values.Remove(key);
        else
            _values[key] = value;
    }

    /// <summary>Resets every slider; the look (style, colours, hair) is kept.</summary>
    public void Reset() => _values.Clear();

    public BodyShape Clone()
    {
        var copy = new BodyShape();
        copy.CopyLookFrom(this);
        foreach (var (key, value) in _values)
            copy._values[key] = value;
        return copy;
    }

    /// <summary>Copies style, colours and hair, but no slider values.</summary>
    public void CopyLookFrom(BodyShape other)
    {
        Style = other.Style;
        SkinColor = other.SkinColor;
        UnderwearColor = other.UnderwearColor;
        EyeColor = other.EyeColor;
        HairColor = other.HairColor;
        Hair = other.Hair;
        Bangs = other.Bangs;
    }

    public bool SameAs(BodyShape other)
    {
        if (Style != other.Style || SkinColor != other.SkinColor || UnderwearColor != other.UnderwearColor
            || EyeColor != other.EyeColor || HairColor != other.HairColor || Hair != other.Hair
            || Bangs != other.Bangs || _values.Count != other._values.Count)
            return false;
        foreach (var (key, value) in _values)
        {
            if (!other._values.TryGetValue(key, out float v) || MathF.Abs(v - value) > 1e-5f)
                return false;
        }
        return true;
    }

    public string ToJson() => JsonSerializer.Serialize(new Document
    {
        FormatVersion = FormatVersion,
        Style = Style,
        SkinColor = SkinColor,
        UnderwearColor = UnderwearColor,
        EyeColor = EyeColor,
        HairColor = HairColor,
        Hair = Hair,
        Bangs = Bangs,
        Body = new SortedDictionary<string, float>(_values),
    }, Json);

    /// <summary>Reads a saved body; unknown sliders and fields are ignored so old files keep loading.</summary>
    public static BodyShape FromJson(string json)
    {
        var doc = JsonSerializer.Deserialize<Document>(json, Json) ?? throw new JsonException("Empty body file");
        if (doc.FormatVersion > FormatVersion)
            throw new JsonException($"Body file format {doc.FormatVersion} is newer than this app supports");
        var shape = new BodyShape();
        if (doc.Style != null)
            shape.Style = doc.Style;
        if (doc.SkinColor != null)
            shape.SkinColor = doc.SkinColor;
        if (doc.UnderwearColor != null)
            shape.UnderwearColor = doc.UnderwearColor;
        if (doc.EyeColor != null)
            shape.EyeColor = doc.EyeColor;
        if (doc.HairColor != null)
            shape.HairColor = doc.HairColor;
        if (doc.Hair != null)
            shape.Hair = doc.Hair;
        if (doc.Bangs != null)
            shape.Bangs = doc.Bangs;
        if (doc.Body != null)
        {
            foreach (var (key, value) in doc.Body)
            {
                if (BodyCatalog.ById.ContainsKey(BaseId(key)))
                    shape.Set(key, value);
            }
        }
        return shape;
    }

    private static string BaseId(string key) =>
        key.EndsWith(".l", StringComparison.Ordinal) || key.EndsWith(".r", StringComparison.Ordinal) ? key[..^2] : key;

    private sealed class Document
    {
        [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; }
        [JsonPropertyName("style")] public string? Style { get; set; }
        [JsonPropertyName("skinColor")] public string? SkinColor { get; set; }
        [JsonPropertyName("underwearColor")] public string? UnderwearColor { get; set; }
        [JsonPropertyName("eyeColor")] public string? EyeColor { get; set; }
        [JsonPropertyName("hairColor")] public string? HairColor { get; set; }
        [JsonPropertyName("hair")] public string? Hair { get; set; }
        [JsonPropertyName("bangs")] public string? Bangs { get; set; }
        [JsonPropertyName("body")] public SortedDictionary<string, float>? Body { get; set; }
    }
}
