using Microsoft.Extensions.Options;
using WindowsAIAssistant.Application.Common.Errors;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Application.Common.Exceptions;
using WindowsAIAssistant.Core.Exceptions;

namespace WindowsAIAssistant.Application.Tests.Errors;

public sealed class ExceptionMapperTests
{
    [Fact]
    public void Map_WithAIServiceException_ReturnsExternalServiceError()
    {
        var error = ExceptionMapper.Map(new AIServiceException("provider timeout"));

        Assert.Equal(ErrorCodes.AiRequestFailed, error.Code);
        Assert.Equal(ErrorType.ExternalService, error.Type);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public void Map_WithWindowsServiceException_ReturnsSystemError()
    {
        var error = ExceptionMapper.Map(new WindowsServiceException("shell call failed"));

        Assert.Equal(ErrorCodes.SystemOperationFailed, error.Code);
        Assert.Equal(ErrorType.System, error.Type);
        Assert.False(error.IsRetryable);
    }

    [Fact]
    public void Map_WithOperationCanceledException_ReturnsCancelledError()
    {
        var error = ExceptionMapper.Map(new OperationCanceledException());

        Assert.Equal(ErrorCodes.OperationCancelled, error.Code);
        Assert.Equal(ErrorType.Cancelled, error.Type);
    }

    [Fact]
    public void Map_WithTaskCanceledException_ReturnsCancelledErrorNotUnknown()
    {
        var error = ExceptionMapper.Map(new TaskCanceledException("caller stopped"));

        Assert.Equal(ErrorCodes.OperationCancelled, error.Code);
        Assert.Equal(ErrorType.Cancelled, error.Type);
        Assert.NotEqual(ErrorType.Unknown, error.Type);
    }

    [Fact]
    public void Map_WithArgumentException_ReturnsValidationError()
    {
        var error = ExceptionMapper.Map(new ArgumentException("bad input", "param"));

        Assert.Equal(ErrorCodes.ValidationError, error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
    }

    [Fact]
    public void Map_WithArgumentNullException_ReturnsValidationError()
    {
        var error = ExceptionMapper.Map(new ArgumentNullException("param"));

        Assert.Equal(ErrorCodes.ValidationError, error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
    }

    [Fact]
    public void Map_WithConversationNotFoundException_ReturnsNotFoundError()
    {
        var error = ExceptionMapper.Map(new ConversationNotFoundException(Guid.NewGuid()));

        Assert.Equal(ErrorCodes.ConversationNotFound, error.Code);
        Assert.Equal(ErrorType.NotFound, error.Type);
    }

    [Fact]
    public void Map_WithKeyNotFoundException_ReturnsNotFoundError()
    {
        var error = ExceptionMapper.Map(new KeyNotFoundException("missing"));

        Assert.Equal(ErrorCodes.NotFound, error.Code);
        Assert.Equal(ErrorType.NotFound, error.Type);
    }

    [Fact]
    public void Map_WithOptionsValidationException_ReturnsConfigurationError()
    {
        var exception = new OptionsValidationException(
            "AIOptions",
            typeof(object),
            ["AI:Provider must not be empty."]);

        var error = ExceptionMapper.Map(exception);

        Assert.Equal(ErrorCodes.ConfigurationInvalid, error.Code);
        Assert.Equal(ErrorType.Configuration, error.Type);
    }

    [Fact]
    public void Map_WithUnauthorizedAccessException_ReturnsPermissionError()
    {
        var error = ExceptionMapper.Map(new UnauthorizedAccessException());

        Assert.Equal(ErrorCodes.PermissionDenied, error.Code);
        Assert.Equal(ErrorType.Permission, error.Type);
    }

    [Fact]
    public void Map_WithIOException_ReturnsRetryableSystemError()
    {
        var error = ExceptionMapper.Map(new IOException("file in use"));

        Assert.Equal(ErrorCodes.IoOperationFailed, error.Code);
        Assert.Equal(ErrorType.System, error.Type);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public void Map_WithUnknownException_ReturnsUnknownError()
    {
        var error = ExceptionMapper.Map(new InvalidCastException("unexpected"));

        Assert.Equal(ErrorCodes.UnknownError, error.Code);
        Assert.Equal(ErrorType.Unknown, error.Type);
        Assert.False(error.IsRetryable);
    }

    [Fact]
    public void Map_WithExceptionContainingSecret_DoesNotExposeSecretInMessage()
    {
        var exception = new AIServiceException("API failed with key sk-secret-example");

        var error = ExceptionMapper.Map(exception);

        Assert.DoesNotContain("sk-secret-example", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-secret-example", error.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_WithSensitivePathInException_DoesNotExposePathInMessage()
    {
        var exception = new WindowsServiceException(
            @"Could not open C:\Users\John\Documents\Private\salary.pdf");

        var error = ExceptionMapper.Map(exception);

        Assert.DoesNotContain("salary.pdf", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("John", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_WithNullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ExceptionMapper.Map(null!));
    }
}
