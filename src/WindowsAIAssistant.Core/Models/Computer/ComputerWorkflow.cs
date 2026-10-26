namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record ComputerWorkflow
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<ComputerWorkflowStep> Steps { get; init; } = [];

    public bool IsSafeMode { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the person has already agreed to every step.
    /// <para>
    /// A workflow is submitted as one thing and approved as one thing, so the individual steps
    /// do not ask again. A caller that has not been through an approval flow leaves this false
    /// and the per-action permission settings decide.
    /// </para>
    /// </summary>
    public bool IsConfirmed { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
}

public sealed record ComputerWorkflowStep
{
    public required ComputerAction Action { get; init; }

    public string? Description { get; init; }

    public TimeSpan WaitAfter { get; init; }

    public bool ContinueOnFailure { get; init; }

    public string? ExpectedElementName { get; init; }

    public string? ExpectedWindowTitle { get; init; }
}
