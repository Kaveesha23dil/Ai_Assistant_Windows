using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Computer;

public sealed record ComputerPermission
{
    public required ComputerActionType ActionType { get; init; }

    public required ComputerPermissionLevel Level { get; init; }

    public required ComputerRiskLevel RiskLevel { get; init; }

    public PermissionCapability? Capability { get; init; }
}
