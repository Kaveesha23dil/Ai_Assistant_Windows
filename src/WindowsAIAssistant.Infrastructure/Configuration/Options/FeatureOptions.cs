namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    public bool EnableAIChat { get; init; } = true;

    public bool EnableFileSearch { get; init; } = true;

    public bool EnableClipboard { get; init; } = true;

    public bool EnableSystemInformation { get; init; } = true;

    public bool EnableAutomation { get; init; }

    public bool EnableVoice { get; init; }

    public bool EnableScreenAI { get; init; }

    public bool EnableLocalAI { get; init; }
}
