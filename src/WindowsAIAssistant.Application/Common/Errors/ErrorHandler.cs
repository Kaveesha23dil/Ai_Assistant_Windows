using Microsoft.Extensions.Logging;

namespace WindowsAIAssistant.Application.Common.Errors;

/// <summary>
/// Central <see cref="IErrorHandler"/> implementation.
/// <para>
/// The exceptions reaching this component originate from first-party code, so the exception
/// object is logged to retain the stack trace. Any future component that may raise an
/// exception carrying remote response data or secrets must log a sanitized message instead
/// of passing the exception object (see <c>SensitiveDataSanitizer</c>).
/// </para>
/// </summary>
public sealed class ErrorHandler : IErrorHandler
{
    private readonly ILogger<ErrorHandler> _logger;

    public ErrorHandler(ILogger<ErrorHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public ApplicationError Handle(Exception exception, string operation)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var error = ExceptionMapper.Map(exception);

        if (error.Type == ErrorType.Cancelled)
        {
            _logger.LogInformation(
                "Operation {Operation} was cancelled. {ErrorCode}",
                operation,
                error.Code);
        }
        else
        {
            _logger.LogError(
                exception,
                "Operation {Operation} failed. {ErrorCode} ({ErrorType})",
                operation,
                error.Code,
                error.Type);
        }

        return error;
    }
}
