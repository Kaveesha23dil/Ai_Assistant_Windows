namespace WindowsAIAssistant.Core.Common;

/// <summary>
/// Stable, user-safe error codes. These values are part of the application contract and
/// must not expose internal class names, file paths, or configuration values.
/// <para>
/// They live in Core because both the Application layer and the Infrastructure layer produce
/// them: a voice command handler and the speech engine underneath it can both fail with
/// <see cref="VoiceRecognitionFailed"/>, and the user interface has to be able to tell those
/// two cases apart from a single shared vocabulary.
/// </para>
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

    public const string VoiceMicrophoneUnavailable = "VOICE_MICROPHONE_UNAVAILABLE";
    public const string VoicePermissionDenied = "VOICE_PERMISSION_DENIED";
    public const string VoiceRecognitionFailed = "VOICE_RECOGNITION_FAILED";
    public const string VoiceCommandNotRecognized = "VOICE_COMMAND_NOT_RECOGNIZED";
    public const string VoiceActionFailed = "VOICE_ACTION_FAILED";
    public const string VoiceActionRestricted = "VOICE_ACTION_RESTRICTED";
    public const string VoiceActionNotAvailable = "VOICE_ACTION_NOT_AVAILABLE";
    public const string VoiceSynthesisFailed = "VOICE_SYNTHESIS_FAILED";
    public const string VoiceBusy = "VOICE_BUSY";
}
