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

    /// <summary>No credential is configured, so a cloud provider was never contacted.</summary>
    public const string AiCredentialMissing = "AI_CREDENTIAL_MISSING";

    /// <summary>The provider rejected the credential.</summary>
    public const string AiAuthenticationFailed = "AI_AUTHENTICATION_FAILED";

    /// <summary>The provider asked the application to slow down.</summary>
    public const string AiRateLimited = "AI_RATE_LIMITED";

    /// <summary>The request exceeded its time budget.</summary>
    public const string AiTimedOut = "AI_TIMED_OUT";

    /// <summary>The provider could not be reached.</summary>
    public const string AiNetworkFailure = "AI_NETWORK_FAILURE";

    /// <summary>The request was rejected as malformed by the provider.</summary>
    public const string AiInvalidRequest = "AI_INVALID_REQUEST";

    /// <summary>The configured provider name is not one this build can serve.</summary>
    public const string AiProviderUnavailable = "AI_PROVIDER_UNAVAILABLE";

    /// <summary>The person has not allowed the request to leave this machine.</summary>
    public const string AiCloudConsentRequired = "AI_CLOUD_CONSENT_REQUIRED";

    public const string InvalidMessage = "INVALID_MESSAGE";
    public const string FileSearchFailed = "FILE_SEARCH_FAILED";
    public const string SystemInfoFailed = "SYSTEM_INFO_FAILED";
    public const string ApplicationLaunchFailed = "APPLICATION_LAUNCH_FAILED";
    public const string ClipboardAccessFailed = "CLIPBOARD_ACCESS_FAILED";

    // Document intelligence. Every code here names a situation a person can act on. None of
    // them describe a file type, a library, or a path: a message paired with these is read
    // aloud by the voice path, so it has to be about the document and not about the machine.
    public const string DocumentNotFound = "DOCUMENT_NOT_FOUND";
    public const string DocumentAccessDenied = "DOCUMENT_ACCESS_DENIED";
    public const string DocumentFormatUnsupported = "DOCUMENT_FORMAT_UNSUPPORTED";
    public const string DocumentTooLarge = "DOCUMENT_TOO_LARGE";
    public const string DocumentPasswordRequired = "DOCUMENT_PASSWORD_REQUIRED";
    public const string DocumentExtractionFailed = "DOCUMENT_EXTRACTION_FAILED";
    public const string DocumentEmpty = "DOCUMENT_EMPTY";
    public const string DocumentOcrRequired = "DOCUMENT_OCR_REQUIRED";
    public const string DocumentAiPermissionDenied = "DOCUMENT_AI_PERMISSION_DENIED";
    public const string DocumentAnalysisFailed = "DOCUMENT_ANALYSIS_FAILED";

    // Knowledge base. These are the situations a person can do something about, kept apart
    // rather than merged into one failure, because the action differs for each: reindex, turn on
    // a permission, add a document, or change the embedding model.
    public const string KnowledgeOperationFailed = "KNOWLEDGE_OPERATION_FAILED";
    public const string KnowledgeBaseNotFound = "KNOWLEDGE_BASE_NOT_FOUND";
    public const string KnowledgeBaseEmpty = "KNOWLEDGE_BASE_EMPTY";
    public const string KnowledgeBaseNameTaken = "KNOWLEDGE_BASE_NAME_TAKEN";
    public const string KnowledgeBaseFull = "KNOWLEDGE_BASE_FULL";
    public const string KnowledgeDocumentAlreadyIndexed = "KNOWLEDGE_DOCUMENT_ALREADY_INDEXED";
    public const string KnowledgeDocumentNotFound = "KNOWLEDGE_DOCUMENT_NOT_FOUND";
    public const string KnowledgeDocumentIndexing = "KNOWLEDGE_DOCUMENT_INDEXING";
    public const string KnowledgeDocumentOutdated = "KNOWLEDGE_DOCUMENT_OUTDATED";
    public const string KnowledgeDocumentReindexRequired = "KNOWLEDGE_DOCUMENT_REINDEX_REQUIRED";
    public const string KnowledgeSourceNotFound = "KNOWLEDGE_SOURCE_NOT_FOUND";
    public const string KnowledgeEmbeddingPermissionDenied = "KNOWLEDGE_EMBEDDING_PERMISSION_DENIED";
    public const string KnowledgeAiPermissionDenied = "KNOWLEDGE_AI_PERMISSION_DENIED";
    public const string KnowledgeEmbeddingFailed = "KNOWLEDGE_EMBEDDING_FAILED";
    public const string KnowledgeEmbeddingUnavailable = "KNOWLEDGE_EMBEDDING_UNAVAILABLE";
    public const string KnowledgeEmbeddingSpaceMismatch = "KNOWLEDGE_EMBEDDING_SPACE_MISMATCH";
    public const string KnowledgeEmbeddingInvalid = "KNOWLEDGE_EMBEDDING_INVALID";
    public const string KnowledgeContextEmpty = "KNOWLEDGE_CONTEXT_EMPTY";
    public const string KnowledgeAnswerFailed = "KNOWLEDGE_ANSWER_FAILED";
    public const string KnowledgeTooManyChunks = "KNOWLEDGE_TOO_MANY_CHUNKS";
    public const string KnowledgeStorageUnavailable = "KNOWLEDGE_STORAGE_UNAVAILABLE";

    /// <summary>
    /// A stored vector could not be read: it is truncated, or it is not in the format this build
    /// writes. The document needs reindexing.
    /// </summary>
    public const string KnowledgeVectorCorrupted = "KNOWLEDGE_VECTOR_CORRUPTED";

    // Screen vision. Split by stage so the person is told what failed, not just that something
    // did: nothing was read, nothing was understood, or nothing was sent. Each names a situation
    // a person can act on, and none of them describe a Windows API, a model, or a file path.
    /// <summary>The Windows capture path is not available on this machine.</summary>
    public const string ScreenCaptureUnsupported = "SCREEN_CAPTURE_UNSUPPORTED";

    /// <summary>The capture itself failed: the target went away, or no frame arrived.</summary>
    public const string ScreenCaptureFailed = "SCREEN_CAPTURE_FAILED";

    /// <summary>
    /// The person dismissed the capture picker. Not a failure: nothing is wrong and nobody is
    /// shown an error, because choosing not to look is a normal thing to do.
    /// </summary>
    public const string ScreenCaptureCancelled = "SCREEN_CAPTURE_CANCELLED";

    /// <summary>The frame arrived blank, which usually means the content is protected.</summary>
    public const string ScreenCaptureEmpty = "SCREEN_CAPTURE_EMPTY";

    /// <summary>Windows refused to hand over the frame because its content is protected.</summary>
    public const string ScreenProtectedContent = "SCREEN_PROTECTED_CONTENT";

    /// <summary>The frame could not be encoded as an image.</summary>
    public const string ScreenImageEncodeFailed = "SCREEN_IMAGE_ENCODE_FAILED";

    /// <summary>The image is larger than this build will send to a model.</summary>
    public const string ScreenImageTooLarge = "SCREEN_IMAGE_TOO_LARGE";

    /// <summary>The image could not be read at all.</summary>
    public const string ScreenImageInvalid = "SCREEN_IMAGE_INVALID";

    /// <summary>No rectangle was chosen, so there is nothing to crop.</summary>
    public const string ScreenRegionEmpty = "SCREEN_REGION_EMPTY";

    /// <summary>The rectangle cannot be applied to the frame it was drawn on.</summary>
    public const string ScreenRegionInvalid = "SCREEN_REGION_INVALID";

    /// <summary>The rectangle lies entirely outside the captured frame.</summary>
    public const string ScreenRegionOutsideFrame = "SCREEN_REGION_OUTSIDE_FRAME";

    // Text recognition. "Unavailable" and "failed" are separate because they are separate
    // decisions: a machine with no recogniser is working as designed, while a recogniser that
    // was present and then errored is something to look at.
    /// <summary>No text engine is installed or usable on this machine.</summary>
    public const string OcrUnavailable = "OCR_UNAVAILABLE";

    /// <summary>A text engine was available and then failed.</summary>
    public const string OcrFailed = "OCR_FAILED";

    /// <summary>The engine ran but read nothing, which is not the same as having failed.</summary>
    public const string OcrNoTextFound = "OCR_NO_TEXT_FOUND";

    /// <summary>Reading text is turned off, so the image was not read.</summary>
    public const string OcrPermissionDenied = "OCR_PERMISSION_DENIED";

    // Visual analysis. The consent codes matter most: they are what a caller branches on to tell
    // "you have not allowed this" apart from "this did not work", and they are checked before
    // any network call is made rather than reported afterwards.
    /// <summary>The configured model cannot accept an image, only text.</summary>
    public const string VisionModelNoImageSupport = "VISION_MODEL_NO_IMAGE_SUPPORT";

    /// <summary>Looking at the screen is turned off, so the image was not read or sent.</summary>
    public const string VisionAnalysisPermissionDenied = "VISION_ANALYSIS_PERMISSION_DENIED";

    /// <summary>Sending screen content to a cloud provider is turned off.</summary>
    public const string VisionCloudPermissionDenied = "VISION_CLOUD_PERMISSION_DENIED";

    /// <summary>No provider is configured that can look at an image.</summary>
    public const string VisionProviderUnavailable = "VISION_PROVIDER_UNAVAILABLE";

    /// <summary>The provider was asked and did not answer.</summary>
    public const string VisionAnalysisFailed = "VISION_ANALYSIS_FAILED";

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
