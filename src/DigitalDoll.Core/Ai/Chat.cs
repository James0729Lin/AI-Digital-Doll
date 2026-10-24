using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalDoll.Core.Ai;

public enum ChatRole
{
    User,
    Assistant,
}

public sealed record ChatTurn(ChatRole Role, string Text);

public sealed record ChatRequest(
    string Model,
    string SystemPrompt,
    IReadOnlyList<ChatTurn> History,
    int MaxTokens = 1024);

public enum ChatErrorKind
{
    /// <summary>Missing or rejected API key.</summary>
    Unauthorized,
    RateLimited,
    /// <summary>The service had a problem (5xx).</summary>
    ServerError,
    /// <summary>No connection (offline, wrong address, local server not running).</summary>
    Network,
    /// <summary>The model declined to answer.</summary>
    Refused,
    /// <summary>Anything else the service rejected (unknown model, bad request…).</summary>
    Other,
}

public sealed class ChatException : Exception
{
    public ChatException(ChatErrorKind kind, string message, Exception? inner = null)
        : base(message, inner) => Kind = kind;

    public ChatErrorKind Kind { get; }
}

/// <summary>One AI service the mascot can talk to.</summary>
public interface IChatProvider
{
    /// <summary>Streams the reply as text fragments.</summary>
    IAsyncEnumerable<string> StreamAsync(ChatRequest request, CancellationToken cancellationToken);

    /// <summary>Model ids the account can use.</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);
}
