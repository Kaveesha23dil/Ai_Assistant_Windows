using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models;

/// <summary>
/// User-facing application settings. Sensitive credentials are deliberately excluded
/// and will be handled by a separate secure storage mechanism.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Gets or sets the active AI provider.</summary>
    public AIProviderType AIProvider { get; set; } = AIProviderType.Unknown;

    /// <summary>Gets or sets the AI model identifier used for chat.</summary>
    public string? AIModel { get; set; }

    /// <summary>Gets or sets the UI theme (for example "Default", "Light" or "Dark").</summary>
    public string Theme { get; set; } = "Default";

    /// <summary>Gets or sets whether the application starts automatically at sign-in.</summary>
    public bool LaunchAtStartup { get; set; }

    /// <summary>Gets or sets whether notifications are enabled.</summary>
    public bool EnableNotifications { get; set; }

    /// <summary>Gets or sets whether local, on-device AI is preferred.</summary>
    public bool UseLocalAI { get; set; }
}