using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Infrastructure.AI;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Development;

/// <summary>
/// The development AI service, kept as a service in its own right.
/// <para>
/// It composes <see cref="MockAIProvider"/> rather than answering for itself, which is what
/// keeps one implementation of the development behaviour in the solution. It exists as a
/// separate service because a host that wants the development assistant without the
/// coordinator — a smoke test, a demo page, a harness that is not testing the wiring — can
/// still ask for an <see cref="IAIService"/> and get the same answers, streaming included.
/// </para>
/// <para>
/// It performs no consent check and no timeout, because there is nothing to consent to and
/// nothing to time out. The production path goes through the coordinator, which does both.
/// </para>
/// </summary>
public sealed class MockAIService : IAIService
{
    private readonly MockAIProvider _provider;

    public MockAIService(IOptionsMonitor<AIOptions> options)
        : this(new MockAIProvider(options))
    {
    }

    public MockAIService(MockAIProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <inheritdoc />
    public AIProviderType ActiveProvider => _provider.ProviderType;

    /// <inheritdoc />
    public Task<AIResponse> SendMessageAsync(
        IReadOnlyCollection<AIMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return _provider.SendMessageAsync(new AIRequest(messages), cancellationToken);
    }

    /// <inheritdoc />
    public Task<AIResponse> SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return _provider.SendMessageAsync(AIRequest.FromMessage(message), cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AIStreamUpdate> StreamMessageAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _provider.StreamMessageAsync(request, cancellationToken);
    }
}
