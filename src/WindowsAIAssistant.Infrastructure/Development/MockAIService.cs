using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Development;

public sealed class MockAIService : IAIService
{
    private const string ResponseText = "AI service is configured correctly.";
    private readonly AIOptions _options;

    public MockAIService(IOptions<AIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public Task<AIResponse> SendMessageAsync(
        IReadOnlyCollection<AIMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var message = messages.LastOrDefault()?.Content;
        var content = string.IsNullOrWhiteSpace(message)
            ? ResponseText
            : $"{ResponseText} Received: {message}";

        return Task.FromResult(AIResponse.Success(content, AIProviderType.Local, _options.Model));
    }

    public Task<AIResponse> SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return SendMessageAsync(new[] { AIMessage.CreateUser(message) }, cancellationToken);
    }
}
