using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Core.Abstractions.Computer;

public interface IComputerActionValidator
{
    Result Validate(ComputerActionRequest request, ComputerSafetyContext safety);

    ComputerRiskLevel GetRiskLevel(ComputerActionType actionType);
}
