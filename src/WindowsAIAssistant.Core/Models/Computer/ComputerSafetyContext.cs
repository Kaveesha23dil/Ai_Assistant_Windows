namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record ComputerSafetyContext
{
    public bool IsComputerControlAllowed { get; init; }

    public bool AllowApplicationLaunch { get; init; }

    public bool AllowWindowControl { get; init; }

    public bool AllowKeyboardInput { get; init; }

    public bool AllowMouseInput { get; init; }

    public bool AllowFormSubmission { get; init; }

    public bool SafeMode { get; init; } = true;

    public bool RequireApprovalForCoordinateClicks { get; init; } = true;

    public int ActionTimeoutSeconds { get; init; } = 10;

    public int MaxActionsPerMinute { get; init; } = 30;

    public IReadOnlyList<string> TrustedApplications { get; init; } = [];

    public IReadOnlyList<string> BlockedApplications { get; init; } = [];
}
