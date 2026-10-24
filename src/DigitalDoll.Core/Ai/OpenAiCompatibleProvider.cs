using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalDoll.Core.Ai;

/// <summary>
/// Chat completions over HTTP with server-sent events: OpenAI, and the services that
/// speak the same format (DeepSeek, Grok, Gemini's compatibility endpoint, Ollama,
/// LM Studio, llama.cpp…).
/// </summary>
public sealed class OpenAiCompatibleProvider : IChatProvider
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromMinutes(5) };

    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly HttpClient _http;

    public OpenAiCompatibleProvider(string baseUrl, string? apiKey, HttpClient? http = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _http = http ?? SharedHttp;
    }

    public async IAsyncEnumerable<string> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = new JsonArray();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt });
        foreach (var turn in request.History)
            messages.Add(new JsonObject { ["role"] = turn.Role == ChatRole.User ? "user" : "assistant", ["content"] = turn.Text });

        // No token limit field: services disagree on its name (max_tokens vs
        // max_completion_tokens) and the persona prompt already asks for short replies.
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["stream"] = true,
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        Authorize(message);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await Send(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (IOException e)
            {
                throw new ChatException(ChatErrorKind.Network, e.Message, e);
            }
            if (line == null)
                yield break;
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var data = line[5..].Trim();
            if (data == "[DONE]")
                yield break;
            if (data.Length == 0)
                continue;

            string? text = ParseDelta(data);
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/models");
        Authorize(message);
        using var response = await Send(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var models = json?["data"]?.AsArray() ?? json?["models"]?.AsArray();
        if (models == null)
            return Array.Empty<string>();
        return models
            .Select(m => (string?)m?["id"] ?? (string?)m?["name"])
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!.StartsWith("models/", StringComparison.Ordinal) ? id[7..] : id)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Text of one streamed chunk, or null when it carries none.</summary>
    public static string? ParseDelta(string data)
    {
        JsonNode? json;
        try
        {
            json = JsonNode.Parse(data);
        }
        catch (JsonException)
        {
            return null;
        }
        if (json?["error"] is JsonNode error)
            throw new ChatException(ChatErrorKind.Other, (string?)error["message"] ?? error.ToJsonString());
        var choice = json?["choices"]?.AsArray().FirstOrDefault();
        if (choice?["finish_reason"] is JsonNode finish && (string?)finish == "content_filter")
            throw new ChatException(ChatErrorKind.Refused, "content_filter");
        var content = choice?["delta"]?["content"];
        return content is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    }

    private void Authorize(HttpRequestMessage message)
    {
        if (_apiKey != null)
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage message, HttpCompletionOption option, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, option, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            throw new ChatException(ChatErrorKind.Network, e.Message, e);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatException(ChatErrorKind.Network, "timeout", e);
        }

        if (response.IsSuccessStatusCode)
            return response;

        string detail = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            detail = (string?)JsonNode.Parse(detail)?["error"]?["message"] ?? detail;
        }
        catch (JsonException)
        {
        }
        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ChatErrorKind.Unauthorized,
            HttpStatusCode.TooManyRequests => ChatErrorKind.RateLimited,
            >= HttpStatusCode.InternalServerError => ChatErrorKind.ServerError,
            _ => ChatErrorKind.Other,
        };
        response.Dispose();
        throw new ChatException(kind, $"{(int)response.StatusCode}: {detail}");
    }
}
