using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Core.Abstractions.Computer;

public interface IComputerPermissionService
{
    ComputerPermission GetPermission(ComputerActionType actionType);

    ComputerPermissionLevel GetLevel(PermissionCapability capability);

    Result EnsureAllowed(ComputerActionType actionType, bool isConfirmed);

    Result EnsureApplicationAllowed(string applicationName);
}
