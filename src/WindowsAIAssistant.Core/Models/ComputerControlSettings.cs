using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models;

public sealed class ComputerControlSettings
{
    public bool AllowComputerControl { get; set; }

    public bool AllowApplicationLaunch { get; set; }

    public bool AllowWindowControl { get; set; }

    public bool AllowKeyboardInput { get; set; }

    public bool AllowMouseInput { get; set; }

    public bool AllowFormSubmission { get; set; }

    public bool SafeMode { get; set; } = true;

    public bool RequireApprovalForCoordinateClicks { get; set; } = true;

    public int ActionTimeoutSeconds { get; set; } = 10;

    public int MaxActionsPerMinute { get; set; } = 30;

    public List<string> TrustedApplications { get; set; } = [];

    public List<string> BlockedApplications { get; set; } = [];

    public Dictionary<string, ComputerPermissionLevel> Permissions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
