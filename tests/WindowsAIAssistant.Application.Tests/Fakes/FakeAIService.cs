using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Tests.Fakes;

public sealed class FakeAIService : IAIService
{
    public AIResponse Response { get; set; } = AIResponse.Success("Test response", AIProviderType.Custom);

    public AIMessage? LastMessage { get; private set; }

    public IReadOnlyCollection<AIMessage>? LastMessages { get; private set; }

    public int CallCount { get; private set; }

    /// <summary>When set, <see cref="SendMessageAsync(IReadOnlyCollection{AIMessage}, CancellationToken)"/> throws this exception.</summary>
    public Exception? ExceptionToThrow { get; set; }

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
        LastMessage = messages.SingleOrDefault();
        return Task.FromResult(Response);
    }

    public Task<AIResponse> SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        var aiMessage = AIMessage.CreateUser(message);
        LastMessage = aiMessage;
        LastMessages = new[] { aiMessage };
        return Task.FromResult(Response);
    }
}
