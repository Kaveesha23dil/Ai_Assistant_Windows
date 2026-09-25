namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

public sealed class ApplicationOptions
{
    public const string SectionName = "Application";

    public string Name { get; init; } = "Windows AI Assistant";

    public string Environment { get; init; } = "Production";

    public bool EnableDiagnostics { get; init; }
}
