namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

public sealed class PrivacyOptions
{
    public const string SectionName = "Privacy";

    public bool AllowCloudAI { get; init; }

    public bool AllowTelemetry { get; init; }

    public bool AllowClipboardProcessing { get; init; }

    public bool AllowFileIndexing { get; init; }

    public bool AllowScreenAnalysis { get; init; }

    public bool StoreConversationHistory { get; init; }
}
