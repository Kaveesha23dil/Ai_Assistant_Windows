using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Tests.Fakes;

/// <summary>
/// A provider under the test's control.
/// <para>
/// Every way a provider can behave badly is a settable property rather than a subclass:
/// unavailable, refusing, throwing, or streaming a specific sequence of updates. Subclassing
/// would need a new type per scenario, and the interesting cases are about which of these is
/// set, not about structure.
/// </para>
/// <para>
/// Nothing here reaches a network, and there is no way to make it do so.
/// </para>
/// </summary>
public sealed class FakeAIProvider : IAIProvider
{
    public FakeAIProvider(
        AIProviderType providerType = AIProviderType.Mock,
        bool isCloudHosted = false,
        string name = "Fake provider")
    {
        ProviderType = providerType;
        IsCloudHosted = isCloudHosted;
        Name = name;
    }

    public string Name { get; }

    public AIProviderType ProviderType { get; }

    public bool IsCloudHosted { get; }

    public bool IsAvailable { get; set; } = true;

    public bool SupportsStreaming { get; set; } = true;

    /// <summary>Gets or sets the answer the whole-answer call returns.</summary>
    public AIResponse Response { get; set; } =
        AIResponse.Success("A fake answer.", AIProviderType.Mock, "fake-model");

    /// <summary>When set, the whole-answer call throws this instead of answering.</summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>
    /// When set, the stream throws this part-way through. A failure after some text has arrived
    /// is the case that matters, because the caller has to decide what to do with the partial
    /// answer it already showed.
    /// </summary>
    public Exception? StreamExceptionAfterFirstDelta { get; set; }

    /// <summary>
    /// When set, the provider waits until its token is cancelled and then throws, which is what a
    /// real provider does when a request runs past its deadline. A fake that merely threw a
    /// cancellation immediately would never let the deadline fire, so it would not exercise the
    /// timeout at all.
    /// </summary>
    public bool HangUntilCancelled { get; set; }

    /// <summary>
    /// When set, the stream yields exactly these updates. When null, the stream is derived from
    /// <see cref="Response"/>.
    /// </summary>
    public IReadOnlyList<AIStreamUpdate>? StreamUpdates { get; set; }

    /// <summary>Gets or sets whether the stream announces its own start. A real provider does,
    /// so this defaults to true and the coordinator's own announcement is the one under test.</summary>
    public bool AnnouncesOwnStart { get; set; } = true;

    /// <summary>Gets or sets the pause between stream updates, so a cancellation has something
    /// to interrupt.</summary>
    public TimeSpan FragmentDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Gets the request the whole-answer call received.</summary>
    public AIRequest? LastRequest { get; private set; }

    /// <summary>Gets the request the stream received.</summary>
    public AIRequest? LastStreamRequest { get; private set; }

    public int SendCount { get; private set; }

    public int StreamCount { get; private set; }

    public async Task<AIResponse> SendMessageAsync(AIRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        SendCount++;
        LastRequest = request;

        if (HangUntilCancelled)
        {
            // The deadline is what ends this, and the cancellation surfaces from the await. That
            // is the only shape that actually tests a timeout.
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Response;
    }

    public async IAsyncEnumerable<AIStreamUpdate> StreamMessageAsync(
        AIRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        StreamCount++;
        LastStreamRequest = request;

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

        if (AnnouncesOwnStart)
        {
            yield return AIStreamUpdate.Started(ProviderType, Response.Model ?? "fake-model");
        }

        if (StreamExceptionAfterFirstDelta is not null)
        {
            var text = Response.Content;
            var cut = text.Length / 2;
            yield return AIStreamUpdate.Delta(text[..cut]);

            await DelayAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            throw StreamExceptionAfterFirstDelta;
        }

        if (!Response.IsSuccessful)
        {
            yield return AIStreamUpdate.Failed(
                string.Empty,
                Response,
                Response.ErrorCode ?? "FAKE_FAILURE",
                Response.ErrorMessage);
            yield break;
        }

        var content = Response.Content;
        foreach (var fragment in Fragments(content))
        {
            await DelayAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            yield return AIStreamUpdate.Delta(fragment);
        }

        yield return AIStreamUpdate.Completed(Response, content);
    }

    private async Task DelayAsync(CancellationToken cancellationToken)
    {
        if (FragmentDelay > TimeSpan.Zero)
        {
            await Task.Delay(FragmentDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Splits on spaces, keeping each space with the word before it, so the fragments joined
    /// back together are byte-for-byte the original answer.
    /// </summary>
    private static IEnumerable<string> Fragments(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOf(' ', start);
            if (end < 0)
            {
                yield return text[start..];
                yield break;
            }

            yield return text[start..(end + 1)];
            start = end + 1;
        }
    }
}

/// <summary>
/// A factory whose answers a test dictates directly, so coordinator tests do not have to
/// configure a provider through options to get one selected.
/// </summary>
public sealed class FakeAIProviderFactory : IAIProviderFactory
{
    private readonly Dictionary<AIProviderType, IAIProvider> _providers = [];

    public IReadOnlyCollection<AIProviderType> AvailableProviders => _providers.Keys.ToArray();

    public AIProviderType SelectedProvider { get; set; } = AIProviderType.Unknown;

    public string? SelectedModel { get; set; }

    public void Register(IAIProvider provider) => _providers[provider.ProviderType] = provider;

    public IAIProvider? Resolve(AIProviderType providerType) =>
        _providers.GetValueOrDefault(providerType);

    public IAIProvider? ResolveSelected() => Resolve(SelectedProvider);
}

/// <summary>
/// An options monitor a test can move at will.
/// <para>
/// The production services read their settings through a monitor so a change is picked up
/// without a restart. That is a promise these tests hold them to: a value changed here has to be
/// visible on the next call, with no new instance anywhere.
/// </para>
/// </summary>
public sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    private T _value = value;

    public T CurrentValue => _value;

    public T Get(string? name) => _value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;

    /// <summary>Replaces the value every later read will report.</summary>
    public void Set(T value) => _value = value;
}
