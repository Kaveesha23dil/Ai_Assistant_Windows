namespace WindowsAIAssistant.Application.Common.Errors;

/// <summary>
/// Logs technical failure context and converts an exception into a user-safe
/// <see cref="ApplicationError"/>.
/// </summary>
public interface IErrorHandler
{
    /// <summary>Handles <paramref name="exception"/> raised during <paramref name="operation"/>.</summary>
    /// <param name="exception">The caught technical exception.</param>
    /// <param name="operation">A short, safe description of what was being attempted.</param>
    /// <returns>A user-safe error that never contains a stack trace or exception message.</returns>
    ApplicationError Handle(Exception exception, string operation);
}
