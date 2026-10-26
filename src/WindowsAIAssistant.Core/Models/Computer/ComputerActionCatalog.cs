using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Computer;

/// <summary>
/// The risk and consent attached to each kind of computer action.
/// <para>
/// The mapping lives in Core rather than in the platform implementation because both the tool
/// that describes an action to a person and the service that runs it must agree about how risky
/// it is. Two copies of the table would eventually disagree, and the one a person was shown
/// would not be the one that decided whether to run.
/// </para>
/// </summary>
public static class ComputerActionCatalog
{
    /// <summary>Gets how much damage an action could do if it was not what the person meant.</summary>
    public static ComputerRiskLevel GetRiskLevel(ComputerActionType actionType) => actionType switch
    {
        ComputerActionType.FindUiElement => ComputerRiskLevel.ReadOnly,
        ComputerActionType.ReadUiElement => ComputerRiskLevel.ReadOnly,
        ComputerActionType.Wait => ComputerRiskLevel.ReadOnly,
        ComputerActionType.Scroll => ComputerRiskLevel.Low,
        ComputerActionType.LaunchApplication => ComputerRiskLevel.Low,
        ComputerActionType.SwitchWindow => ComputerRiskLevel.Low,
        ComputerActionType.MinimizeWindow => ComputerRiskLevel.Low,
        ComputerActionType.MaximizeWindow => ComputerRiskLevel.Low,
        ComputerActionType.RestoreWindow => ComputerRiskLevel.Low,
        ComputerActionType.Focus => ComputerRiskLevel.Low,
        ComputerActionType.Click => ComputerRiskLevel.Medium,
        ComputerActionType.DoubleClick => ComputerRiskLevel.Medium,
        ComputerActionType.RightClick => ComputerRiskLevel.Medium,
        ComputerActionType.TypeText => ComputerRiskLevel.Medium,
        ComputerActionType.PressKey => ComputerRiskLevel.Medium,
        ComputerActionType.PressShortcut => ComputerRiskLevel.Medium,
        ComputerActionType.Select => ComputerRiskLevel.Medium,
        ComputerActionType.CloseApplication => ComputerRiskLevel.High,
        _ => ComputerRiskLevel.Critical
    };

    /// <summary>Gets the consent switch an action depends on.</summary>
    public static PermissionCapability? GetCapability(ComputerActionType actionType) => actionType switch
    {
        ComputerActionType.LaunchApplication => PermissionCapability.ComputerApplicationLaunch,
        ComputerActionType.CloseApplication => PermissionCapability.ComputerWindowControl,
        ComputerActionType.SwitchWindow => PermissionCapability.ComputerWindowControl,
        ComputerActionType.MinimizeWindow => PermissionCapability.ComputerWindowControl,
        ComputerActionType.MaximizeWindow => PermissionCapability.ComputerWindowControl,
        ComputerActionType.RestoreWindow => PermissionCapability.ComputerWindowControl,
        ComputerActionType.Focus => PermissionCapability.ComputerWindowControl,
        ComputerActionType.TypeText => PermissionCapability.ComputerKeyboardInput,
        ComputerActionType.PressKey => PermissionCapability.ComputerKeyboardInput,
        ComputerActionType.PressShortcut => PermissionCapability.ComputerKeyboardInput,
        ComputerActionType.Click => PermissionCapability.ComputerMouseInput,
        ComputerActionType.DoubleClick => PermissionCapability.ComputerMouseInput,
        ComputerActionType.RightClick => PermissionCapability.ComputerMouseInput,
        ComputerActionType.Scroll => PermissionCapability.ComputerMouseInput,
        ComputerActionType.Select => PermissionCapability.ComputerMouseInput,
        ComputerActionType.FindUiElement => PermissionCapability.ComputerControl,
        ComputerActionType.ReadUiElement => PermissionCapability.ComputerControl,
        ComputerActionType.Wait => PermissionCapability.ComputerControl,
        _ => PermissionCapability.ComputerControl
    };

    /// <summary>
    /// Reads an action name as the enum member it names, ignoring case and the spaces somebody
    /// might have put between the words.
    /// </summary>
    public static bool TryParse(string? value, out ComputerActionType actionType)
    {
        actionType = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        return Enum.TryParse(normalized, ignoreCase: true, out actionType)
            && Enum.IsDefined(actionType);
    }
}
