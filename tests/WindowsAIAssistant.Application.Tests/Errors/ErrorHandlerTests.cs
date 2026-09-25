using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Common.Errors;
using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Exceptions;

namespace WindowsAIAssistant.Application.Tests.Errors;

public sealed class ErrorHandlerTests
{
    [Fact]
    public void Handle_WithServiceFailure_LogsErrorAndReturnsSafeError()
    {
        var logger = new TestLogger<ErrorHandler>();
        var handler = new ErrorHandler(logger);

        var error = handler.Handle(new AIServiceException("boom"), "SendMessage");

        Assert.Equal(ErrorCodes.AiRequestFailed, error.Code);
        Assert.Equal(ErrorType.ExternalService, error.Type);
        Assert.True(logger.Contains(LogLevel.Error, "SendMessage"));
        Assert.True(logger.Contains(LogLevel.Error, ErrorCodes.AiRequestFailed));
        Assert.Contains(logger.Entries, entry => entry.Exception is AIServiceException);
    }

    [Fact]
    public void Handle_WithCancellation_LogsInformationNotError()
    {
        var logger = new TestLogger<ErrorHandler>();
        var handler = new ErrorHandler(logger);

        var error = handler.Handle(new OperationCanceledException(), "FileSearch");

        Assert.Equal(ErrorType.Cancelled, error.Type);
        Assert.True(logger.Contains(LogLevel.Information, ErrorCodes.OperationCancelled));
        Assert.False(logger.ContainsLevel(LogLevel.Error));
    }

    [Fact]
    public void Handle_WithExceptionContainingSecret_DoesNotReturnSecretMessage()
    {
        var handler = new ErrorHandler(new TestLogger<ErrorHandler>());

        var error = handler.Handle(
            new AIServiceException("request failed with token sk-secret-example"),
            "SendMessage");

        Assert.DoesNotContain("sk-secret-example", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Handle_WithNullException_ThrowsArgumentNullException()
    {
        var handler = new ErrorHandler(new TestLogger<ErrorHandler>());

        Assert.Throws<ArgumentNullException>(() => handler.Handle(null!, "SendMessage"));
    }

    [Fact]
    public void Handle_WithEmptyOperation_ThrowsArgumentException()
    {
        var handler = new ErrorHandler(new TestLogger<ErrorHandler>());

        Assert.Throws<ArgumentException>(() => handler.Handle(new InvalidOperationException(), " "));
    }
}
