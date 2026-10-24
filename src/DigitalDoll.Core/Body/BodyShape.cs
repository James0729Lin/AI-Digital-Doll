using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalDoll.Core.Body;

/// <summary>
/// The editable body: one value per slider key (mirrored sliders have ".l" and ".r"
/// keys). Values that equal the slider default are not stored.
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

    public string SkinColor { get; set; } = "#E0AC8A";
    public string UnderwearColor { get; set; } = "#3A3F4B";

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

    public void Reset() => _values.Clear();

    public BodyShape Clone()
    {
        var copy = new BodyShape { SkinColor = SkinColor, UnderwearColor = UnderwearColor };
        foreach (var (key, value) in _values)
            copy._values[key] = value;
        return copy;
    }

    public bool SameAs(BodyShape other)
    {
        if (SkinColor != other.SkinColor || UnderwearColor != other.UnderwearColor || _values.Count != other._values.Count)
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
        SkinColor = SkinColor,
        UnderwearColor = UnderwearColor,
        Body = new SortedDictionary<string, float>(_values),
    }, Json);

    /// <summary>Reads a saved body; unknown sliders are ignored so old files keep loading.</summary>
    public static BodyShape FromJson(string json)
    {
        var doc = JsonSerializer.Deserialize<Document>(json, Json) ?? throw new JsonException("Empty body file");
        if (doc.FormatVersion > FormatVersion)
            throw new JsonException($"Body file format {doc.FormatVersion} is newer than this app supports");
        var shape = new BodyShape();
        if (doc.SkinColor != null)
            shape.SkinColor = doc.SkinColor;
        if (doc.UnderwearColor != null)
            shape.UnderwearColor = doc.UnderwearColor;
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
        [JsonPropertyName("skinColor")] public string? SkinColor { get; set; }
        [JsonPropertyName("underwearColor")] public string? UnderwearColor { get; set; }
        [JsonPropertyName("body")] public SortedDictionary<string, float>? Body { get; set; }
    }
}
