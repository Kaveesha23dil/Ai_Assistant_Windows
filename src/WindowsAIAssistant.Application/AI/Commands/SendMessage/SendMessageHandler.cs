using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Common.Validation;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.AI.Commands.SendMessage;

public sealed class SendMessageHandler
{
    private readonly IAIService _aiService;
    private readonly ILogger<SendMessageHandler> _logger;

    public SendMessageHandler(IAIService aiService, ILogger<SendMessageHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(aiService);
        ArgumentNullException.ThrowIfNull(logger);
        _aiService = aiService;
        _logger = logger;
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

        _logger.LogInformation("AI message request started.");
        try
        {
            var message = AIMessage.CreateUser(content);
            IReadOnlyCollection<AIMessage> messages = new[] { message };

            var response = await _aiService
                .SendMessageAsync(messages, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("AI message request completed using provider {Provider}.", response.Provider);
            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("AI message request cancelled by caller.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "AI message request failed.");
            throw;
        }
    }
}
