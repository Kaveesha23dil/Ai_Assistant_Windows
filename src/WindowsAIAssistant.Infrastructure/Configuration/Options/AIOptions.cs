namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

public sealed class AIOptions
{
    public const string SectionName = "AI";

    public string Provider { get; init; } = "Mock";

    public string Model { get; init; } = "mock-model";

    public double Temperature { get; init; } = 0.7;

    public int MaxOutputTokens { get; init; } = 2048;

    public int RequestTimeoutSeconds { get; init; } = 60;

    public bool UseStreaming { get; init; }
}
