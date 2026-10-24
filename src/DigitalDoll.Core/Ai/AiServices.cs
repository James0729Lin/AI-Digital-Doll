using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalDoll.Core.Ai;

public enum ProviderProtocol
{
    /// <summary>OpenAI's chat completions format, also spoken by DeepSeek, Grok, Gemini, Ollama, LM Studio…</summary>
    OpenAiCompatible,

    /// <summary>Anthropic's Messages API (Claude), through the official SDK.</summary>
    Anthropic,
}

/// <summary>A ready-made service entry: where it lives and how to talk to it.</summary>
public sealed record ServicePreset(
    string Id,
    string Name,
    ProviderProtocol Protocol,
    string BaseUrl,
    bool NeedsKey,
    string? SuggestedModel,
    string KeyHelpUrl);

/// <summary>A service the user has set up. The API key is stored separately (encrypted).</summary>
public sealed class ProviderConfig
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("preset")] public string Preset { get; set; } = "custom";
    [JsonPropertyName("protocol")] public ProviderProtocol Protocol { get; set; } = ProviderProtocol.OpenAiCompatible;
    [JsonPropertyName("baseUrl")] public string BaseUrl { get; set; } = "";
    [JsonPropertyName("model")] public string Model { get; set; } = "";

    /// <summary>Short handle for "@name" routing in the chat box (lower case, no spaces).</summary>
    public string Handle => new string(Name.ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
}

/// <summary>The AI part of the user's settings.</summary>
public sealed class AiSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [JsonPropertyName("providers")] public List<ProviderConfig> Providers { get; set; } = new();
    [JsonPropertyName("defaultProvider")] public string? DefaultProvider { get; set; }
    [JsonPropertyName("characterName")] public string CharacterName { get; set; } = "小桃";
    [JsonPropertyName("personality")] public string Personality { get; set; } = "活潑、溫柔、說話自然簡短，偶爾俏皮";

    public ProviderConfig? Default =>
        Providers.FirstOrDefault(p => p.Id == DefaultProvider) ?? Providers.FirstOrDefault();

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static AiSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new AiSettings();
        try
        {
            return JsonSerializer.Deserialize<AiSettings>(json, Json) ?? new AiSettings();
        }
        catch (JsonException)
        {
            return new AiSettings();
        }
    }

    /// <summary>
    /// Splits "@handle question" into the provider it names and the question. Without a
    /// known handle the default provider gets the whole text.
    /// </summary>
    public (ProviderConfig? Provider, string Text) Route(string input)
    {
        var text = input.Trim();
        if (text.StartsWith('@'))
        {
            int space = text.IndexOf(' ');
            string handle = (space < 0 ? text[1..] : text[1..space]).ToLowerInvariant();
            var match = Providers.FirstOrDefault(p => p.Handle == handle)
                ?? Providers.FirstOrDefault(p => p.Handle.StartsWith(handle, StringComparison.Ordinal) && handle.Length > 0);
            if (match != null)
                return (match, space < 0 ? "" : text[(space + 1)..].Trim());
        }
        return (Default, text);
    }
}

public static class AiServices
{
    public static readonly IReadOnlyList<ServicePreset> Presets = new[]
    {
        new ServicePreset("anthropic", "Claude", ProviderProtocol.Anthropic, "https://api.anthropic.com", true,
            "claude-opus-5-5", "https://platform.claude.com/settings/keys"),
        new ServicePreset("openai", "ChatGPT", ProviderProtocol.OpenAiCompatible, "https://api.openai.com/v1", true,
            null, "https://platform.openai.com/api-keys"),
        new ServicePreset("gemini", "Gemini", ProviderProtocol.OpenAiCompatible, "https://generativelanguage.googleapis.com/v1beta/openai", true,
            null, "https://aistudio.google.com/apikey"),
        new ServicePreset("deepseek", "DeepSeek", ProviderProtocol.OpenAiCompatible, "https://api.deepseek.com/v1", true,
            null, "https://platform.deepseek.com/api_keys"),
        new ServicePreset("grok", "Grok", ProviderProtocol.OpenAiCompatible, "https://api.x.ai/v1", true,
            null, "https://console.x.ai"),
        new ServicePreset("ollama", "Ollama（本地）", ProviderProtocol.OpenAiCompatible, "http://localhost:11434/v1", false,
            null, "https://ollama.com/download"),
        new ServicePreset("lmstudio", "LM Studio（本地）", ProviderProtocol.OpenAiCompatible, "http://localhost:1234/v1", false,
            null, "https://lmstudio.ai"),
        new ServicePreset("custom", "自訂（OpenAI 相容）", ProviderProtocol.OpenAiCompatible, "", true, null, ""),
    };

    public static ServicePreset? Preset(string id) => Presets.FirstOrDefault(p => p.Id == id);

    public static ProviderConfig FromPreset(ServicePreset preset) => new()
    {
        Name = preset.Name.Split('（')[0],
        Preset = preset.Id,
        Protocol = preset.Protocol,
        BaseUrl = preset.BaseUrl,
        Model = preset.SuggestedModel ?? "",
    };

    public static IChatProvider Create(ProviderConfig config, string? apiKey, HttpClient? http = null) => config.Protocol switch
    {
        ProviderProtocol.Anthropic => new ClaudeProvider(config.BaseUrl, apiKey ?? ""),
        _ => new OpenAiCompatibleProvider(config.BaseUrl, apiKey, http),
    };
}
