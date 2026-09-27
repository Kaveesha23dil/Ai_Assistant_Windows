using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.AI;

/// <summary>
/// The provider that answers without leaving the machine.
/// <para>
/// It exists so the application can be built, demonstrated, and tested with no account, no
/// key, and no network, and it is a first-class provider rather than a special case in the
/// coordinator: the streaming path, the consent check, and the error translation are all
/// exercised by using it, which means the code that runs in development is the code that runs
/// in production. Only the transport is different.
/// </para>
/// <para>
/// It answers in fragments like a network provider would, so a caller that mishandles
/// streaming fails here rather than in front of a person.
/// </para>
/// </summary>
public sealed class MockAIProvider : IAIProvider
{
    private const string ResponseText = "AI service is configured correctly.";

    /// <summary>
    /// The answer is emitted in fragments like a network provider would, and each fragment
    /// carries its trailing space with it. Concatenating them reproduces the sentence exactly,
    /// which is what a caller must be able to rely on.
    /// </summary>
    private const int WordsPerFragment = 4;

    private readonly IOptionsMonitor<AIOptions> _options;

    public MockAIProvider(IOptionsMonitor<AIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// The model name to report. Read per request so an edited setting is honoured without a
    /// restart, matching how the real providers report the model they were asked for.
    /// </summary>
    private string Model => _options.CurrentValue.Model;

    /// <inheritdoc />
    public string Name => "Development assistant (no account needed)";

    /// <inheritdoc />
    public AIProviderType ProviderType => AIProviderType.Mock;

    /// <inheritdoc />
    public bool IsCloudHosted => false;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public bool SupportsStreaming => true;

    /// <inheritdoc />
    public async Task<AIResponse> SendMessageAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // A small pause makes cancellation observable in a test without needing a network.
        await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);

        return AIResponse.Success(BuildContent(request.Messages), ProviderType, Model);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AIStreamUpdate> StreamMessageAsync(
        AIRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        yield return AIStreamUpdate.Started(ProviderType, Model);

        var received = new StringBuilder();
        foreach (var fragment in Fragments(BuildContent(request.Messages)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The pause is what makes a stop mid-answer reproducible, and it is short enough
            // that the development experience still feels immediate.
            await Task.Delay(TimeSpan.FromMilliseconds(15), cancellationToken).ConfigureAwait(false);

            received.Append(fragment);
            yield return AIStreamUpdate.Delta(fragment);
        }

        yield return AIStreamUpdate.Completed(
            AIResponse.Success(received.ToString(), ProviderType, Model),
            received.ToString());
    }

    /// <summary>
    /// Answers with the last question the person asked. The system instruction is skipped: a
    /// request always opens with one, and echoing it back would report the prompt as the
    /// conversation.
    /// </summary>
    private static string BuildContent(IReadOnlyCollection<AIMessage> messages)
    {
        var message = messages
            .LastOrDefault(candidate => candidate.Role == AIMessageRole.User)
            ?.Content;

        return string.IsNullOrWhiteSpace(message)
            ? ResponseText
            : $"{ResponseText} Received: {message}";
    }

    /// <summary>
    /// Splits text into fragments, keeping the separator on the preceding one so the pieces
    /// concatenate back to the original.
    /// </summary>
    private static IEnumerable<string> Fragments(string content)
    {
        var start = 0;
        var count = 0;

        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] != ' ')
            {
                continue;
            }

            count++;
            if (count < WordsPerFragment)
            {
                continue;
            }

            yield return content[start..(index + 1)];
            start = index + 1;
            count = 0;
        }

        if (start < content.Length)
        {
            yield return content[start..];
        }
    }
}
