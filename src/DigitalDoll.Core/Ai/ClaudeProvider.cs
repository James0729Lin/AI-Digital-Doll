using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using AnthropicBeta = Anthropic.Models.Beta.AnthropicBeta;

namespace DigitalDoll.Core.Ai;

/// <summary>
/// Claude through the official Anthropic SDK. Uses low effort (a desktop companion
/// should answer quickly) and server-side refusal fallbacks so a declined reply is
/// re-served by a fallback model within the same call.
/// </summary>
public sealed class ClaudeProvider : IChatProvider
{
    private readonly AnthropicClient _client;

    public ClaudeProvider(string baseUrl, string apiKey)
    {
        _client = string.IsNullOrWhiteSpace(baseUrl)
            ? new AnthropicClient { ApiKey = apiKey }
            : new AnthropicClient { ApiKey = apiKey, BaseUrl = baseUrl };
    }

    public async IAsyncEnumerable<string> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens,
            System = request.SystemPrompt,
            Messages = request.History.Select(ToParam).ToList(),
            OutputConfig = new BetaOutputConfig { Effort = Effort.Low },
            Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
            Fallbacks = new Default(),
        };

        var events = _client.Beta.Messages.CreateStreaming(parameters, cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                BetaRawMessageStreamEvent streamEvent;
                try
                {
                    if (!await events.MoveNextAsync())
                        yield break;
                    streamEvent = events.Current;
                }
                catch (Exception e) when (e is not OperationCanceledException && e is not ChatException)
                {
                    throw Translate(e);
                }

                if (streamEvent.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
                    yield return text.Text;
                else if (streamEvent.TryPickDelta(out var messageDelta) && messageDelta.Delta.StopReason == "refusal")
                    throw new ChatException(ChatErrorKind.Refused, "refusal");
            }
        }
        finally
        {
            await events.DisposeAsync();
        }
    }

    private static BetaMessageParam ToParam(ChatTurn turn) => turn.Role == ChatRole.User
        ? new BetaUserMessageParam { Content = turn.Text }
        : new BetaAssistantMessageParam { Content = turn.Text };

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var page = await _client.Models.List(cancellationToken: cancellationToken);
            return page.Items.Select(m => m.ID).ToList();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw Translate(e);
        }
    }

    private static ChatException Translate(Exception e) => e switch
    {
        AnthropicUnauthorizedException => new ChatException(ChatErrorKind.Unauthorized, e.Message, e),
        AnthropicRateLimitException => new ChatException(ChatErrorKind.RateLimited, e.Message, e),
        Anthropic5xxException => new ChatException(ChatErrorKind.ServerError, e.Message, e),
        AnthropicIOException => new ChatException(ChatErrorKind.Network, e.Message, e),
        AnthropicApiException => new ChatException(ChatErrorKind.Other, e.Message, e),
        System.Net.Http.HttpRequestException => new ChatException(ChatErrorKind.Network, e.Message, e),
        _ => new ChatException(ChatErrorKind.Other, e.Message, e),
    };
}
