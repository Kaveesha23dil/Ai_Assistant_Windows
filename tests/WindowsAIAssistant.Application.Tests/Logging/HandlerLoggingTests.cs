using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.AI.Commands.SendMessage;
using WindowsAIAssistant.Application.Clipboard.Queries.GetClipboardText;
using WindowsAIAssistant.Application.Files.Queries.SearchFiles;
using WindowsAIAssistant.Application.System.Queries.GetSystemInformation;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Exceptions;

namespace WindowsAIAssistant.Application.Tests.Logging;

public sealed class HandlerLoggingTests
{
    [Fact]
    public async Task SendMessage_OnSuccess_LogsStartedAndCompletedWithoutMessageContent()
    {
        var logger = new TestLogger<SendMessageHandler>();
        var handler = new SendMessageHandler(new FakeAIService(), logger);

        await handler.HandleAsync(new SendMessageCommand("my private salary question"));

        Assert.True(logger.Contains(LogLevel.Information, "AI message request started"));
        Assert.True(logger.Contains(LogLevel.Information, "AI message request completed"));
        Assert.False(logger.ContainsMessage("salary"));
        Assert.False(logger.ContainsMessage("my private salary question"));
    }

    [Fact]
    public async Task SendMessage_OnFailure_LogsErrorAndRethrows()
    {
        var logger = new TestLogger<SendMessageHandler>();
        var service = new FakeAIService { ExceptionToThrow = new AIServiceException("provider down") };
        var handler = new SendMessageHandler(service, logger);

        await Assert.ThrowsAsync<AIServiceException>(
            () => handler.HandleAsync(new SendMessageCommand("Hello")));

        Assert.True(logger.Contains(LogLevel.Error, "AI message request failed"));
        Assert.Contains(logger.Entries, entry => entry.Exception is AIServiceException);
    }

    [Fact]
    public async Task SendMessage_OnCancellation_LogsInformationNotError()
    {
        var logger = new TestLogger<SendMessageHandler>();
        var service = new FakeAIService { ExceptionToThrow = new OperationCanceledException() };
        var handler = new SendMessageHandler(service, logger);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.HandleAsync(new SendMessageCommand("Hello")));

        Assert.True(logger.Contains(LogLevel.Information, "AI message request cancelled"));
        Assert.False(logger.ContainsLevel(LogLevel.Error));
    }

    [Fact]
    public async Task FileSearch_LogsResultCountWithoutQueryOrPaths()
    {
        var logger = new TestLogger<SearchFilesHandler>();
        var service = new FakeFileSearchService();
        var handler = new SearchFilesHandler(service, logger);

        await handler.HandleAsync(new SearchFilesQuery("confidential payroll"));

        Assert.True(logger.Contains(LogLevel.Information, "File search started"));
        Assert.True(logger.Contains(LogLevel.Information, "File search completed"));
        Assert.False(logger.ContainsMessage("payroll"));
    }

    [Fact]
    public async Task GetSystemInformation_LogsCompletionWithoutHardwareDetails()
    {
        var logger = new TestLogger<GetSystemInformationHandler>();
        var service = new FakeWindowsSystemService();
        var handler = new GetSystemInformationHandler(service, logger);

        await handler.HandleAsync(new GetSystemInformationQuery());

        Assert.True(logger.Contains(LogLevel.Information, "System information request started"));
        Assert.True(logger.Contains(LogLevel.Information, "System information request completed"));
        Assert.False(logger.ContainsMessage("TEST-MACHINE"));
        Assert.False(logger.ContainsMessage("tester"));
    }

    [Fact]
    public async Task GetClipboardText_LogsSuccessWithoutContent()
    {
        var logger = new TestLogger<GetClipboardTextHandler>();
        var service = new FakeClipboardService { Text = "password123" };
        var handler = new GetClipboardTextHandler(service, logger);

        await handler.HandleAsync(new GetClipboardTextQuery());

        Assert.True(logger.Contains(LogLevel.Information, "Clipboard text retrieved successfully"));
        Assert.False(logger.ContainsMessage("password123"));
    }
}
