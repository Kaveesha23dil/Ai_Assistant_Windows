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
    Custom
}