namespace WindowsAIAssistant.Application.Common.Errors;

/// <summary>
/// Stable, user-safe error codes. These values are part of the application contract and
/// must not expose internal class names, file paths, or configuration values.
/// </summary>
public static class ErrorCodes
{
    public const string AiRequestFailed = "AI_REQUEST_FAILED";
    public const string InvalidMessage = "INVALID_MESSAGE";
    public const string FileSearchFailed = "FILE_SEARCH_FAILED";
    public const string SystemInfoFailed = "SYSTEM_INFO_FAILED";
    public const string ApplicationLaunchFailed = "APPLICATION_LAUNCH_FAILED";
    public const string ClipboardAccessFailed = "CLIPBOARD_ACCESS_FAILED";
    public const string ConfigurationInvalid = "CONFIGURATION_INVALID";
    public const string ConversationNotFound = "CONVERSATION_NOT_FOUND";
    public const string NotFound = "NOT_FOUND";
    public const string IoOperationFailed = "IO_OPERATION_FAILED";
    public const string OperationCancelled = "OPERATION_CANCELLED";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string SystemOperationFailed = "SYSTEM_OPERATION_FAILED";
    public const string UnknownError = "UNKNOWN_ERROR";
    public const string ValidationError = "VALIDATION_ERROR";
}
