using WindowsAIAssistant.Application.Common.Validation;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.AI.Commands.SendMessage;

public sealed class SendMessageHandler
{
    private readonly IAIService _aiService;

    public SendMessageHandler(IAIService aiService)
    {
        ArgumentNullException.ThrowIfNull(aiService);
        _aiService = aiService;
    }

    public async Task<AIResponse> HandleAsync(
        SendMessageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var content = ValidationHelper.RequireText(
            command.Message,
            nameof(command.Message),
            "Message cannot be empty.");
        var message = AIMessage.CreateUser(content);
        IReadOnlyCollection<AIMessage> messages = new[] { message };

        var response = await _aiService
            .SendMessageAsync(messages, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return response;
    }
}
