using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.AI;

/// <summary>
/// Talks to OpenAI, and is the only type in the solution that knows it.
/// <para>
/// The client is built per request rather than kept for the life of the process. That costs an
/// allocation and buys two things worth more: the key is read at the moment it is needed and
/// is not held in a field afterwards, and a settings change takes effect on the next request
/// without restarting the application. The key goes straight to the client and is never stored,
/// logged, or placed in a message.
/// </para>
/// <para>
/// The provider translates messages, calls the SDK, and translates failures. It writes no
/// prompt text, decides nothing about consent or history, and executes nothing it is given:
/// generated text is content that is displayed, and the system prompt says so before the
/// model ever sees the conversation.
/// </para>
/// </summary>
public sealed class OpenAIProvider : IAIProvider
{
    private readonly IAIApiKeyProvider _apiKey;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ILogger<OpenAIProvider> _logger;

    public OpenAIProvider(IAIApiKeyProvider apiKey, IOptionsMonitor<AIOptions> options, ILogger<OpenAIProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _apiKey = apiKey;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// The settings as they stand for this request. Read per call rather than captured, so an
    /// edited model or token limit is honoured by the next request instead of needing a
    /// restart.
    /// </summary>
    private AIOptions Options => _options.CurrentValue;

    /// <inheritdoc />
    public string Name => "OpenAI";

    /// <inheritdoc />
    public AIProviderType ProviderType => AIProviderType.OpenAI;

    /// <inheritdoc />
    public bool IsCloudHosted => true;

    /// <inheritdoc />
    public bool IsAvailable => _apiKey.HasApiKey;

    /// <inheritdoc />
    public bool SupportsStreaming => true;

    /// <inheritdoc />
    public async Task<AIResponse> SendMessageAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var model = ResolveModel(request);

        try
        {
            var client = CreateClient(model);
            var completion = await client
                .CompleteChatAsync(ToChatMessages(request.Messages), BuildOptions(request), cancellationToken)
                .ConfigureAwait(false);

            var content = ReadText(completion.Value.Content);

            // A completion with no text is a real outcome: a content filter or a refusal can
            // produce one. Reporting it as an empty success would leave the person looking at
            // a blank bubble with no idea why.
            return string.IsNullOrWhiteSpace(content)
                ? AIResponse.Failure(
                    "The AI provider returned an empty answer. Try asking in a different way.",
                    ProviderType,
                    ErrorCodes.AiRequestFailed,
                    model)
                : AIResponse.Success(content, ProviderType, model);
        }
        catch (OperationCanceledException)
        {
            // The caller's own decision, or a timeout the coordinator owns. Either way it is
            // not a provider failure and must not be reported as one.
            throw;
        }
        catch (Exception exception)
        {
            throw Classify(exception);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AIStreamUpdate> StreamMessageAsync(
        AIRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var model = ResolveModel(request);
        var content = new StringBuilder();

        // The client is built before anything is yielded so that a missing key is reported
        // immediately, rather than after a person has been shown an empty answer bubble.
        ChatClient client;
        try
        {
            client = CreateClient(model);
        }
        catch (Exception exception)
        {
            throw Classify(exception);
        }

        IAsyncEnumerator<StreamingChatCompletionUpdate> updates;
        try
        {
            updates = client
                .CompleteChatStreamingAsync(
                    ToChatMessages(request.Messages),
                    BuildOptions(request),
                    cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Classify(exception);
        }

        yield return AIStreamUpdate.Started(ProviderType, model);

        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await updates.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The caller's own decision, or a timeout the coordinator owns.
                    throw;
                }
                catch (Exception exception)
                {
                    throw Classify(exception);
                }

                if (!hasNext)
                {
                    break;
                }

                var delta = ReadText(updates.Current.ContentUpdate);
                if (string.IsNullOrEmpty(delta))
                {
                    // A chunk can carry a role, a finish reason, or usage rather than text.
                    // There is nothing to show for it, and inventing something would be a lie.
                    continue;
                }

                content.Append(delta);
                yield return AIStreamUpdate.Delta(delta);
            }

            yield return AIStreamUpdate.Completed(
                AIResponse.Success(content.ToString(), ProviderType, model),
                content.ToString());
        }
        finally
        {
            await updates.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds a client for one request, or reports that there is no credential.
    /// <para>
    /// The message names the environment variable and not the key, so somebody reading the
    /// chat window learns what to fix without the value appearing anywhere.
    /// </para>
    /// </summary>
    private ChatClient CreateClient(string model)
    {
        var apiKey = _apiKey.GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "No API key is available in {Variable}, so the request was not sent.",
                Options.ApiKeyEnvironmentVariable);

            throw new AIServiceException(
                $"No API key is configured. Set the {Options.ApiKeyEnvironmentVariable} environment variable and try again.",
                ErrorCodes.AiCredentialMissing);
        }

        return new ChatClient(model, apiKey);
    }

    /// <summary>
    /// Reduces a provider failure to a safe one.
    /// <para>
    /// A failure nothing here recognises becomes a generic request failure rather than
    /// escaping as a type the interface has never heard of. Logging records the status and the
    /// code; the raw exception never reaches the chat window or a spoken reply.
    /// </para>
    /// </summary>
    private Exception Classify(Exception exception)
    {
        var classified = OpenAIErrorMapper.Classify(exception);
        if (classified is null)
        {
            _logger.LogError(exception, "The OpenAI provider failed in an unexpected way.");
            return new AIServiceException(
                "The assistant could not complete that request. Try again in a moment.",
                ErrorCodes.AiRequestFailed,
                exception);
        }

        _logger.LogWarning(
            "The OpenAI provider rejected the request with code {ErrorCode} and status {Status}.",
            classified.ErrorCode,
            (exception as ClientResultException)?.Status);

        return classified;
    }

    /// <summary>
    /// Gets the model for a request: the one it names, otherwise the configured one. The
    /// configured value is validated at start-up, so a missing model cannot reach the network.
    /// </summary>
    private string ResolveModel(AIRequest request)
    {
        var requested = request.Settings.Model;
        return string.IsNullOrWhiteSpace(requested) ? Options.Model : requested;
    }

    private ChatCompletionOptions BuildOptions(AIRequest request) => new()
    {
        Temperature = (float)(request.Settings.Temperature ?? Options.Temperature),
        MaxOutputTokenCount = request.Settings.MaxOutputTokens ?? Options.MaxOutputTokens
    };

    /// <summary>
    /// Converts the conversation into the provider's own message shape. Roles map one to one
    /// and nothing is added or dropped.
    /// </summary>
    private static ChatMessage[] ToChatMessages(IReadOnlyCollection<AIMessage> messages)
    {
        var converted = new ChatMessage[messages.Count];
        var index = 0;

        foreach (var message in messages)
        {
            converted[index++] = message.Role switch
            {
                AIMessageRole.System => ChatMessage.CreateSystemMessage(message.Content),
                AIMessageRole.Assistant => ChatMessage.CreateAssistantMessage(message.Content),
                _ => ChatMessage.CreateUserMessage(message.Content)
            };
        }

        return converted;
    }

    /// <summary>
    /// Reads the text out of a content block, which is a list of parts rather than a string.
    /// Only text parts are taken: an image or audio part has no text, and asking for one would
    /// return nothing where there is nothing to show.
    /// </summary>
    private static string ReadText(ChatMessageContent? content)
    {
        if (content is null || content.Count == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (var part in content)
        {
            if (part.Kind == ChatMessageContentPartKind.Text && !string.IsNullOrEmpty(part.Text))
            {
                text.Append(part.Text);
            }
        }

        return text.ToString();
    }
}
