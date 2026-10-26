using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Infrastructure.Computer;

/// <summary>
/// Kept as the Infrastructure-facing name for the risk table. The table itself lives in Core so
/// the tool that asks a person to approve an action and the service that runs it read the same
/// answer.
/// </summary>
public static class ComputerActionRisk
{
    public static ComputerRiskLevel GetRiskLevel(ComputerActionType actionType) =>
        ComputerActionCatalog.GetRiskLevel(actionType);

    public static PermissionCapability? GetCapability(ComputerActionType actionType) =>
        ComputerActionCatalog.GetCapability(actionType);
}
