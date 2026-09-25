namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

public sealed class UIOptions
{
    public const string SectionName = "UI";

    public string Theme { get; init; } = "System";

    public string DefaultPage { get; init; } = "Home";

    public bool ShowSystemTrayIcon { get; init; } = true;

    public bool LaunchMinimized { get; init; }
}
