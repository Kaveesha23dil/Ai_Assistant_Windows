namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Identifies a site-specific web search provider. Adding a provider means adding a value
/// here plus an implementation, with no change to the voice command handlers.
/// </summary>
public enum WebSearchProviderType
{
    /// <summary>The general-purpose default search engine.</summary>
    Default,

    /// <summary>Google web search.</summary>
    Google,

    /// <summary>YouTube video search.</summary>
    YouTube,

    /// <summary>GitHub repository and code search.</summary>
    GitHub,

    /// <summary>Stack Overflow question search.</summary>
    StackOverflow
}
