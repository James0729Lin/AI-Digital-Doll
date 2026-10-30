using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalDoll.Core.Ai;

/// <summary>One message in the chat history.</summary>
public sealed record ChatEntry
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("role")]
    public ChatRole Role { get; init; }

    /// <summary>What was shown (an AI reply without its performance cues).</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    /// <summary>What the AI said, cues included (sent back to it as context); empty for the user.</summary>
    [JsonPropertyName("raw")]
    public string Raw { get; init; } = "";

    /// <summary>The AI that answered (its name in the settings); empty for the user.</summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = "";

    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; init; }
}

/// <summary>
/// The whole chat history, kept across runs so it can be read back and deleted message
/// by message. The AI is given only the recent part of it (see <see cref="Context"/>),
/// so a deleted message is forgotten by the AI too.
/// </summary>
public sealed class ChatLog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly List<ChatEntry> _entries = new();
    private long _nextId = 1;

    public ChatLog(int maxEntries = 2000) => MaxEntries = maxEntries;

    /// <summary>The oldest messages are dropped past this many.</summary>
    public int MaxEntries { get; }

    public IReadOnlyList<ChatEntry> Entries => _entries;

    /// <summary>Raised after any change (a message added, deleted, or all cleared).</summary>
    public event Action? Changed;

    public ChatEntry AddUser(string text, DateTimeOffset time) =>
        Add(new ChatEntry { Role = ChatRole.User, Text = text, Time = time });

    public ChatEntry AddAssistant(string text, string raw, string source, DateTimeOffset time) =>
        Add(new ChatEntry { Role = ChatRole.Assistant, Text = text, Raw = raw, Source = source, Time = time });

    public bool Remove(long id)
    {
        int removed = _entries.RemoveAll(e => e.Id == id);
        if (removed > 0)
            Changed?.Invoke();
        return removed > 0;
    }

    public void Clear()
    {
        _entries.Clear();
        Changed?.Invoke();
    }

    /// <summary>The id the next message will get (to start a fresh context from).</summary>
    public long NextId => _nextId;

    /// <summary>The turns to give the AI: the messages from <paramref name="sinceId"/> on.</summary>
    public IEnumerable<ChatTurn> Context(long sinceId) =>
        _entries.Where(e => e.Id >= sinceId)
            .Select(e => new ChatTurn(e.Role, e.Role == ChatRole.Assistant && e.Raw.Length > 0 ? e.Raw : e.Text));

    public string ToJson() => JsonSerializer.Serialize(_entries, Json);

    /// <summary>Reads a saved history; a missing or damaged file gives an empty one.</summary>
    public static ChatLog FromJson(string? json, int maxEntries = 2000)
    {
        var log = new ChatLog(maxEntries);
        if (string.IsNullOrWhiteSpace(json))
            return log;
        try
        {
            var entries = JsonSerializer.Deserialize<List<ChatEntry>>(json, Json);
            if (entries != null)
            {
                foreach (var e in entries.Where(e => e.Text.Length > 0).OrderBy(e => e.Id))
                    log._entries.Add(e);
                log._nextId = log._entries.Count > 0 ? log._entries.Max(e => e.Id) + 1 : 1;
                log.Trim();
            }
        }
        catch (JsonException)
        {
        }
        return log;
    }

    private ChatEntry Add(ChatEntry entry)
    {
        entry = entry with { Id = _nextId++ };
        _entries.Add(entry);
        Trim();
        Changed?.Invoke();
        return entry;
    }

    private void Trim()
    {
        if (_entries.Count > MaxEntries)
            _entries.RemoveRange(0, _entries.Count - MaxEntries);
    }
}
