using Microsoft.Extensions.Options;
using WindowsAIAssistant.Application.Common.Exceptions;
using WindowsAIAssistant.Core.Exceptions;

namespace WindowsAIAssistant.Application.Common.Errors;

/// <summary>
/// Converts technical exceptions into user-safe <see cref="ApplicationError"/> values.
/// Mapped messages are fixed literals: exception messages, stack traces, file paths, and
/// configuration values are never copied into the result.
/// </summary>
public static class ExceptionMapper
{
    public static ApplicationError Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            OperationCanceledException => Cancelled(),
            ConversationNotFoundException => new ApplicationError(
                ErrorCodes.ConversationNotFound,
                "The requested conversation was not found.",
                ErrorType.NotFound),
            KeyNotFoundException => new ApplicationError(
                ErrorCodes.NotFound,
                "The requested item was not found.",
                ErrorType.NotFound),
            OptionsValidationException => new ApplicationError(
                ErrorCodes.ConfigurationInvalid,
                "The application configuration is invalid.",
                ErrorType.Configuration),
            AIServiceException => new ApplicationError(
                ErrorCodes.AiRequestFailed,
                "The AI service could not complete the request.",
                ErrorType.ExternalService,
                isRetryable: true),
            WindowsServiceException => new ApplicationError(
                ErrorCodes.SystemOperationFailed,
                "The Windows operation could not be completed.",
                ErrorType.System),
            UnauthorizedAccessException => new ApplicationError(
                ErrorCodes.PermissionDenied,
                "Access to the requested resource was denied.",
                ErrorType.Permission),
            ArgumentException => new ApplicationError(
                ErrorCodes.ValidationError,
                "The provided input is not valid.",
                ErrorType.Validation),
            IOException => new ApplicationError(
                ErrorCodes.IoOperationFailed,
                "A file operation could not be completed.",
                ErrorType.System,
                isRetryable: true),
            _ => Unknown()
        };
    }

    private static ApplicationError Cancelled() => new(
        ErrorCodes.OperationCancelled,
        "The operation was cancelled.",
        ErrorType.Cancelled);

    private static ApplicationError Unknown() => new(
        ErrorCodes.UnknownError,
        "An unexpected error occurred.",
        ErrorType.Unknown);
}
