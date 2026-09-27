using System.Runtime.CompilerServices;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Tests.Fakes;

/// <summary>
/// A stand-in for <see cref="IAIService"/>.
/// <para>
/// The whole-answer calls return <see cref="Response"/> as given, so a test can set an exact
/// answer or an exact failure. The streaming call is deliberately configurable rather than
/// merely derived from <see cref="Response"/>: streaming is where the interesting behaviour
/// lives, and a fake that could only produce a happy path could not test a provider that stops
/// halfway, fails after three fragments, or is cancelled mid-answer.
/// </para>
/// <para>
/// Nothing here talks to a network. Every answer is written in this file.
/// </para>
/// </summary>
public sealed class FakeAIService : IAIService
{
    public AIResponse Response { get; set; } = AIResponse.Success("Test response", AIProviderType.Custom);

    public AIMessage? LastMessage { get; private set; }

    public IReadOnlyCollection<AIMessage>? LastMessages { get; private set; }

    public int CallCount { get; private set; }

    /// <summary>
    /// Gets the request the coordinator would have been given. Only the whole-answer overloads
    /// have a request to capture, so this is set by those.
    /// </summary>
    public AIRequest? LastRequest { get; private set; }

    /// <summary>
    /// Gets or sets the provider this service claims to be using. Defaults to whatever
    /// <see cref="Response"/> reports, so a test that does not care about the provider does not
    /// have to say so.
    /// </summary>
    public AIProviderType Provider { get; set; } = AIProviderType.Custom;

    /// <summary>When set, the whole-answer call throws this exception.</summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>
    /// When set, the stream throws this instead of running to completion. Used to check that a
    /// provider failure part-way through an answer keeps whatever had already arrived.
    /// </summary>
    public Exception? StreamExceptionToThrow { get; set; }

    /// <summary>
    /// Gets or sets the exact updates a stream should yield. When left null the stream is built
    /// from <see cref="Response"/>: one start, the answer in word-sized fragments, and a
    /// completion carrying the whole thing.
    /// </summary>
    public IReadOnlyList<AIStreamUpdate>? StreamUpdates { get; set; }

    /// <summary>
    /// When set, the stream pauses this long between updates. It exists so a cancellation test
    /// has something to interrupt rather than racing an answer that has already finished.
    /// </summary>
    public TimeSpan FragmentDelay { get; set; } = TimeSpan.Zero;

    AIProviderType IAIService.ActiveProvider => Provider;

    public Task<AIResponse> SendMessageAsync(
        IReadOnlyCollection<AIMessage> messages,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        LastMessages = messages;

        // The last message, not the only one: a request now carries the system instructions
        // ahead of the question, and taking Single would throw on any real conversation.
        LastMessage = messages.LastOrDefault();
        return Task.FromResult(Response);
    }

    public Task<AIResponse> SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Routed through the same path as a real request rather than repeating it, so the two
        // overloads cannot disagree: an override set for a failure test has to be honoured
        // whichever one a caller happens to use.
        return SendMessageAsync(new[] { AIMessage.CreateUser(message) }, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AIStreamUpdate> StreamMessageAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        LastRequest = request;
        LastMessages = request.Messages;
        LastMessage = request.Messages.LastOrDefault();

        return Core(request, cancellationToken);
    }

    private async IAsyncEnumerable<AIStreamUpdate> Core(
        AIRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CallCount++;
        if (StreamExceptionToThrow is not null)
        {
            throw StreamExceptionToThrow;
        }

        if (StreamUpdates is not null)
        {
            foreach (var update in StreamUpdates)
            {
                await DelayAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
            }

            yield break;
        }

        var response = Response;
        var model = response.Model ?? "fake-model";

        yield return AIStreamUpdate.Started(response.Provider, model);

        if (response.IsSuccessful)
        {
            var text = response.Content;
            foreach (var fragment in Fragments(text))
            {
                await DelayAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                yield return AIStreamUpdate.Delta(fragment);
            }

            yield return AIStreamUpdate.Completed(response, text);
            yield break;
        }

        yield return AIStreamUpdate.Failed(
            string.Empty,
            response,
            response.ErrorCode ?? "FAKE_FAILURE",
            response.ErrorMessage);
    }

    private async Task DelayAsync(CancellationToken cancellationToken)
    {
        if (FragmentDelay > TimeSpan.Zero)
        {
            await Task.Delay(FragmentDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Splits into fragments that keep their trailing space, so joining them reproduces the text
    /// exactly. A test that reassembles a stream and compares it to the original answer would
    /// otherwise fail on whitespace alone.
    /// </summary>
    private static IEnumerable<string> Fragments(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        const int wordsPerFragment = 3;
        var start = 0;

        while (start < text.Length)
        {
            var end = start;
            for (var counted = 0; counted < wordsPerFragment && end < text.Length; end++)
            {
                if (text[end] == ' ')
                {
                    counted++;
                }
            }

            yield return text[start..end];
            start = end;
        }
    }
}
