using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

public sealed class ComputerControlOptions
{
    public const string SectionName = "ComputerControl";

    public bool AllowComputerControl { get; init; }

    public bool AllowApplicationLaunch { get; init; }

    public bool AllowWindowControl { get; init; }

    public bool AllowKeyboardInput { get; init; }

    public bool AllowMouseInput { get; init; }

    public bool AllowFormSubmission { get; init; }

    public bool SafeMode { get; init; } = true;

    public bool RequireApprovalForCoordinateClicks { get; init; } = true;

    public int ActionTimeoutSeconds { get; init; } = 10;

    public int MaxActionsPerMinute { get; init; } = 30;

    public string[] TrustedApplications { get; init; } = [];

    public string[] BlockedApplications { get; init; } = [];

    public Dictionary<string, ComputerPermissionLevel> Permissions { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public ComputerSafetyContext ToSafetyContext() => new()
    {
        IsComputerControlAllowed = AllowComputerControl,
        AllowApplicationLaunch = AllowApplicationLaunch,
        AllowWindowControl = AllowWindowControl,
        AllowKeyboardInput = AllowKeyboardInput,
        AllowMouseInput = AllowMouseInput,
        AllowFormSubmission = AllowFormSubmission,
        SafeMode = SafeMode,
        RequireApprovalForCoordinateClicks = RequireApprovalForCoordinateClicks,
        ActionTimeoutSeconds = ActionTimeoutSeconds,
        MaxActionsPerMinute = MaxActionsPerMinute,
        TrustedApplications = TrustedApplications,
        BlockedApplications = BlockedApplications
    };
}
