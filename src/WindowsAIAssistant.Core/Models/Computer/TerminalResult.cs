namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record TerminalResult
{
    public required bool Succeeded { get; init; }

    public string Output { get; init; } = string.Empty;

    public string? Error { get; init; }

    public int ExitCode { get; init; }
}
