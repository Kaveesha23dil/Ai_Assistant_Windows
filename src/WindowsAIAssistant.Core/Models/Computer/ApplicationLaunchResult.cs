namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record ApplicationLaunchResult
{
    public required string ApplicationName { get; init; }

    public int? ProcessId { get; init; }

    public long? WindowHandle { get; init; }

    public bool WasAlreadyRunning { get; init; }

    public TimeSpan Duration { get; init; }
}
