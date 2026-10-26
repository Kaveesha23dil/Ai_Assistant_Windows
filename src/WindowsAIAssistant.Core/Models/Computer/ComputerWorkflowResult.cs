namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record ComputerWorkflowResult
{
    public required string WorkflowName { get; init; }

    public required bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public int CompletedSteps { get; init; }

    public int TotalSteps { get; init; }

    public int? FailedStep { get; init; }

    public IReadOnlyList<ComputerActionResult> StepResults { get; init; } = [];

    public TimeSpan Duration { get; init; }

    public bool WasCancelled { get; init; }
}
