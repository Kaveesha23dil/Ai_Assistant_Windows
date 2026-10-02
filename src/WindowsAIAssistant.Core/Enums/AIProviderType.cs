namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Identifies the AI provider that generated a response.
/// </summary>
public enum AIProviderType
{
    /// <summary>Provider has not been assigned or recognized yet.</summary>
    Unknown,

    /// <summary>OpenAI-compatible cloud provider.</summary>
    OpenAI,

    /// <summary>Azure OpenAI provider.</summary>
    AzureOpenAI,

    /// <summary>Local, on-device inference provider.</summary>
    Local,

    /// <summary>A custom or plug-in provider.</summary>
    Custom,

    /// <summary>
    /// The built-in development provider. It answers without leaving the machine, which is
    /// what lets the application be built, demonstrated, and tested with no account, no key,
    /// and no network.
    /// </summary>
    Mock,

    /// <summary>
    /// Google Gemini cloud provider accessed via the AI Assistant Cloud Backend.
    /// </summary>
    GeminiCloud
}