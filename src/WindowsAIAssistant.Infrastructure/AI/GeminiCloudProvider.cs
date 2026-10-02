using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.AI;

/// <summary>
/// Reaches Gemini through the AI Assistant Cloud Backend.
/// <para>
/// The desktop application holds no Gemini credential. This provider talks only to the cloud
/// backend the operator deploys, which is the single place that holds the Google credential and
/// the single place that knows how to reach the model. That is what makes the vendor key
/// impossible to extract from an installed copy of the application.
/// </para>
/// <para>
/// The provider translates the assistant's request into the backend's contract and translates
/// the backend's answer back. It streams by reading the server-sent events the backend produces,
/// which is the same shape the assistant already renders for any streaming provider.
/// </para>
/// </summary>
public sealed class GeminiCloudProvider : IAIProvider
{
    /// <summary>The named client the factory hands out, so the handler pool is shared.</summary>
    public const string HttpClientName = "GeminiCloud";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ILogger<GeminiCloudProvider> _logger;

    public GeminiCloudProvider(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<AIOptions> options,
        ILogger<GeminiCloudProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    private AIOptions Options => _options.CurrentValue;

    /// <inheritdoc />
    public string Name => "GeminiCloud";

    /// <inheritdoc />
    public AIProviderType ProviderType => AIProviderType.GeminiCloud;

    /// <inheritdoc />
    public bool IsCloudHosted => true;

    /// <inheritdoc />
    public bool IsAvailable => !string.IsNullOrWhiteSpace(Options.CloudApiBaseUrl);

    /// <inheritdoc />
    public bool SupportsStreaming => true;

    /// <inheritdoc />
    public async Task<AIResponse> SendMessageAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var model = Options.Model;

        try
        {
            using var client = CreateClient();
            var payload = new ChatRequestBody(
                LastUserContent(request),
                Guid.NewGuid().ToString(),
                UseKnowledge: false);

            using var response = await client
                .PostAsJsonAsync("/api/chat", payload, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var failure = await ReadFailureAsync(response, cancellationToken).ConfigureAwait(false);
                return AIResponse.Failure(failure, ProviderType, ErrorCodes.AiRequestFailed, model);
            }

            var body = await response.Content
                .ReadFromJsonAsync<ChatResponseBody>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (body is null || string.IsNullOrWhiteSpace(body.Answer))
            {
                return AIResponse.Failure(
                    "The AI service returned an empty answer. Try asking in a different way.",
                    ProviderType,
                    ErrorCodes.AiRequestFailed,
                    body?.Model ?? model);
            }

            return AIResponse.Success(body.Answer, ProviderType, body.Model ?? model);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The exception can name the host or a request field, so nothing from it reaches the
            // person. The type is recorded so the cause is findable.
            _logger.LogError(exception, "A Gemini cloud request failed.");
            return AIResponse.Failure(
                "The cloud AI service is not reachable right now. Local capabilities are still available.",
                ProviderType,
                ErrorCodes.AiRequestFailed,
                model);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AIStreamUpdate> StreamMessageAsync(
        AIRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var model = Options.Model;

        HttpClient client;
        HttpResponseMessage response;
        try
        {
            client = CreateClient();
            var payload = new ChatRequestBody(
                LastUserContent(request),
                Guid.NewGuid().ToString(),
                UseKnowledge: false);

            response = await client
                .PostAsJsonAsync("/api/chat/stream", payload, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A Gemini cloud stream request failed.");
            throw new AIServiceException(
                "The cloud AI service is not reachable right now. Local capabilities are still available.",
                ErrorCodes.AiRequestFailed,
                exception);
        }

        yield return AIStreamUpdate.Started(ProviderType, model);

        // The reader is opened and driven by methods that are not iterators, so a read failure
        // can be translated here without a yield appearing inside a catch clause. The stream is
        // disposed by the using block whether the loop finishes or throws.
        var finished = false;

        using (response)
        {
            await using var stream = await OpenStreamAsync(response, cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (!finished)
            {
                StreamEventBody? @event;

                try
                {
                    @event = await ReadNextEventAsync(reader, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "A Gemini cloud stream read failed.");
                    throw new AIServiceException(
                        "The cloud AI stream was interrupted.",
                        ErrorCodes.AiRequestFailed,
                        exception);
                }

                if (@event is null)
                {
                    break;
                }

                switch (@event.Kind)
                {
                    case "delta" when !string.IsNullOrEmpty(@event.Text):
                        yield return AIStreamUpdate.Delta(@event.Text);
                        break;

                    case "answer" when !string.IsNullOrEmpty(@event.Text):
                        yield return AIStreamUpdate.Completed(
                            AIResponse.Success(@event.Text, ProviderType, model),
                            @event.Text);
                        finished = true;
                        break;

                    case "error":
                        var code = @event.ErrorCode ?? ErrorCodes.AiRequestFailed;
                        var message = @event.Text ?? "The cloud AI stream reported an error.";
                        yield return AIStreamUpdate.Failed(
                            string.Empty,
                            AIResponse.Failure(message, ProviderType, code, model),
                            code,
                            message);
                        finished = true;
                        break;

                    case "done":
                        yield return AIStreamUpdate.Completed(
                            AIResponse.Success(string.Empty, ProviderType, model),
                            string.Empty);
                        finished = true;
                        break;
                }
            }
        }

        if (!finished)
        {
            yield return AIStreamUpdate.Completed(
                AIResponse.Success(string.Empty, ProviderType, model),
                string.Empty);
        }
    }

    private static async Task<Stream> OpenStreamAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the next server-sent event, returning <see langword="null"/> at the end of the body.
    /// <para>
    /// Kept out of the iterator so a malformed line can be skipped and a transport failure can be
    /// translated where it happens, rather than forcing the whole stream to be buffered.
    /// </para>
    /// </summary>
    private static async Task<StreamEventBody?> ReadNextEventAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)
                || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line[5..].Trim();

            if (string.IsNullOrWhiteSpace(data) || data == "[DONE]")
            {
                continue;
            }

            try
            {
                return JsonSerializer.Deserialize<StreamEventBody>(data, JsonOptions);
            }
            catch (JsonException)
            {
                // A malformed event is skipped rather than ending an otherwise good stream.
            }
        }

        return null;
    }

    private static string LastUserContent(AIRequest request)
    {
        foreach (var message in request.Messages.Reverse())
        {
            if (message.Role == AIMessageRole.User
                && !string.IsNullOrWhiteSpace(message.Content))
            {
                return message.Content;
            }
        }

        return string.Empty;
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        var baseUrl = Options.CloudApiBaseUrl;

        client.BaseAddress = string.IsNullOrWhiteSpace(baseUrl)
            ? new Uri("http://localhost:8000/")
            : new Uri(baseUrl.TrimEnd('/') + "/");

        // The token is read here and handed to the client header. It is never stored on the
        // provider or logged, so it lives only as long as the client that needed it.
        var variableName = Options.CloudApiKeyEnvironmentVariable;

        if (!string.IsNullOrWhiteSpace(variableName))
        {
            var token = Environment.GetEnvironmentVariable(variableName);

            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Trim());
            }
        }

        client.Timeout = TimeSpan.FromSeconds(Math.Max(Options.RequestTimeoutSeconds, 5));

        return client;
    }

    private static async Task<string> ReadFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // The backend returns a JSON object with a ``detail`` field. A body that cannot be read
        // falls back to the status code, which is still actionable.
        try
        {
            var body = await response.Content
                .ReadFromJsonAsync<ErrorBody>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(body?.Detail))
            {
                return body.Detail;
            }
        }
        catch (Exception)
        {
            // Deliberately swallowed: the status line below is the fallback and is enough.
        }

        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                "The cloud AI service refused the request. Check that the application is signed in.",
            System.Net.HttpStatusCode.TooManyRequests =>
                "Too many requests were sent. Wait a moment and try again.",
            System.Net.HttpStatusCode.ServiceUnavailable =>
                "The cloud AI service is not reachable right now. Local capabilities are still available.",
            _ => "The cloud AI service could not complete that request.",
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record ChatRequestBody(string Message, string ConversationId, bool UseKnowledge);

    private sealed class ChatResponseBody
    {
        [JsonPropertyName("answer")]
        public string? Answer { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("tokensUsed")]
        public int TokensUsed { get; set; }

        [JsonPropertyName("latencyMs")]
        public int LatencyMs { get; set; }
    }

    private sealed class StreamEventBody
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("errorCode")]
        public string? ErrorCode { get; set; }
    }

    private sealed class ErrorBody
    {
        [JsonPropertyName("detail")]
        public string? Detail { get; set; }
    }
}
