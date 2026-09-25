namespace WindowsAIAssistant.Application.Common.Errors;

/// <summary>
/// Classifies an <see cref="ApplicationError"/> so callers can react without inspecting
/// exception types or technical details.
/// </summary>
public enum ErrorType
{
    /// <summary>Input or arguments were invalid.</summary>
    Validation,

    /// <summary>A requested resource does not exist.</summary>
    NotFound,

    /// <summary>Application configuration is missing or invalid.</summary>
    Configuration,

    /// <summary>An external dependency such as an AI provider failed.</summary>
    ExternalService,

    /// <summary>A Windows or system-level operation failed.</summary>
    System,

    /// <summary>The operation was denied by the operating system.</summary>
    Permission,

    /// <summary>The operation was cancelled, usually by the caller.</summary>
    Cancelled,

    /// <summary>The failure could not be classified.</summary>
    Unknown
}
