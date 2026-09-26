using WindowsAIAssistant.Application.Common.Errors;

namespace WindowsAIAssistant.Application.Tests.Errors;

public sealed class ApplicationErrorTests
{
    [Fact]
    public void Constructor_WithValidValues_ExposesProperties()
    {
        var error = new ApplicationError(
            ErrorCodes.AiRequestFailed,
            "The AI service could not complete the request.",
            ErrorType.ExternalService,
            isRetryable: true);

        Assert.Equal(ErrorCodes.AiRequestFailed, error.Code);
        Assert.Equal("The AI service could not complete the request.", error.Message);
        Assert.Equal(ErrorType.ExternalService, error.Type);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public void Constructor_WithoutRetryability_IsNotRetryable()
    {
        var error = new ApplicationError(ErrorCodes.ValidationError, "Invalid.", ErrorType.Validation);

        Assert.False(error.IsRetryable);
    }

    [Theory]
    [InlineData(null, "message", ErrorType.Unknown)]
    [InlineData("  ", "message", ErrorType.Unknown)]
    [InlineData("CODE", null, ErrorType.Unknown)]
    [InlineData("CODE", "  ", ErrorType.Unknown)]
    public void Constructor_WithMissingCodeOrMessage_Throws(string? code, string? message, ErrorType type)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ApplicationError(code!, message!, type));
    }

    [Fact]
    public void Equality_UsesCodeMessageAndType()
    {
        var first = new ApplicationError(ErrorCodes.UnknownError, "Unexpected.", ErrorType.Unknown);
        var second = new ApplicationError(ErrorCodes.UnknownError, "Unexpected.", ErrorType.Unknown);
        var different = new ApplicationError(ErrorCodes.UnknownError, "Unexpected.", ErrorType.Cancelled);

        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
    }
}
