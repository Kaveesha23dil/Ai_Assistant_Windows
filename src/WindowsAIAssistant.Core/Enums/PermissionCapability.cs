namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// A capability the assistant must hold before performing an action. Every entry maps to a
/// consent switch the user controls, so a capability is never implicitly granted.
/// </summary>
public enum PermissionCapability
{
    /// <summary>Capture audio from the microphone for speech recognition.</summary>
    Microphone,

    /// <summary>Read or write text on the Windows clipboard.</summary>
    Clipboard,

    /// <summary>Search the local file system on the user's behalf.</summary>
    FileSearch,

    /// <summary>Capture the screen to an image file.</summary>
    ScreenCapture,

    /// <summary>Send captured screen content to an AI service for analysis.</summary>
    ScreenAnalysis,

    /// <summary>Change system-level state such as output volume or mute.</summary>
    SystemControl,

    /// <summary>Launch applications and open shell URIs.</summary>
    ApplicationLaunch,

    /// <summary>Run a web search in the default browser.</summary>
    WebSearch,

    /// <summary>Send a transcript or question to a cloud AI provider.</summary>
    CloudAI,

    /// <summary>Retain voice command history for later inspection.</summary>
    VoiceHistory,

    /// </summary>
    /// Send the text of a document to a cloud AI provider.
    /// <para>
    /// Kept apart from <see cref="CloudAI"/> on purpose. Allowing an assistant to answer a
    /// question is not the same decision as allowing it to read a file: a document is
    /// something a person chose to keep, often by choosing where to keep it, and agreeing to
    /// have a question answered says nothing about which files may be opened and sent
    /// elsewhere.
    /// </para>
    /// </summary>
    DocumentCloudProcessing,

    /// <summary>
    /// Keep a local index of the person's documents so questions can be answered across them.
    /// <para>
    /// On by default, and unlike the three below it is a local act: reading, splitting, and
    /// storing text on this machine does not send anything anywhere. It is still a capability
    /// rather than an absence of one, because a person who never wants their files indexed
    /// should be able to say so in one place rather than by never opening the page.
    /// </para>
    /// </summary>
    KnowledgeBase,

    /// <summary>
    /// Send text to a cloud service to be turned into an embedding.
    /// <para>
    /// Separate from <see cref="CloudAI"/> and from <see cref="DocumentCloudProcessing"/>, and
    /// the most easily-missed of the three. Turning on cloud chat says a question may be sent;
    /// turning on document processing says passages of a file may be sent. Neither says anything
    /// about the intermediate step, which sends every passage of every indexed document to a
    /// third party as a vector, on the way to being stored beside the document it came from.
    /// </para>
    /// <para>
    /// A vector is not anonymous. It is a lossy summary of the text that produced it, and a
    /// party holding a few hundred of them for a person's documents knows a great deal about
    /// those documents.
    /// </para>
    /// </summary>
    CloudEmbedding,

    /// <summary>
    /// Send text retrieved from the knowledge base to a cloud AI provider to be answered.
    /// <para>
    /// Required even when the embeddings were made on this machine, because the step that needs
    /// it is the one that puts the passages in front of a model. Where the embeddings went and
    /// where the answer is computed are two different questions, and both have to be answered
    /// yes.
    /// </para>
    /// </summary>
    CloudKnowledgeProcessing
}
