using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Infrastructure.Computer;

public sealed class ComputerActionValidator : IComputerActionValidator
{
    private static readonly HashSet<ComputerActionType> SafeModeActions =
    [
        ComputerActionType.LaunchApplication,
        ComputerActionType.SwitchWindow,
        ComputerActionType.FindUiElement,
        ComputerActionType.ReadUiElement,
        ComputerActionType.Scroll,
        ComputerActionType.Wait
    ];

    public Result Validate(ComputerActionRequest request, ComputerSafetyContext safety)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(safety);

        var action = request.Action;
        var risk = GetRiskLevel(action.Type);

        if (!safety.IsComputerControlAllowed)
        {
            return Result.Failure(
                ErrorCodes.ComputerControlDisabled,
                "Computer control is turned off in Settings.");
        }

        if (risk == ComputerRiskLevel.Critical)
        {
            return Result.Failure(
                ErrorCodes.ComputerUnsafeAction,
                "That computer action is destructive and is not available.");
        }

        if (safety.SafeMode && !SafeModeActions.Contains(action.Type))
        {
            return Result.Failure(
                ErrorCodes.ComputerUnsafeAction,
                "Safe mode allows only launching, switching windows, reading the screen, scrolling, and waiting.");
        }

        if (!IsCapabilityEnabled(action.Type, safety))
        {
            return Result.Failure(
                ErrorCodes.ComputerPermissionDenied,
                "The permission this computer action needs is turned off in Settings.");
        }

        if (RequiresText(action.Type) && string.IsNullOrEmpty(action.Text))
        {
            return Result.Failure(
                ErrorCodes.ComputerActionFailed,
                "There was no text to type.");
        }

        if ((action.Type is ComputerActionType.PressKey or ComputerActionType.PressShortcut)
            && string.IsNullOrWhiteSpace(action.Key)
            && action.Keys.Count == 0)
        {
            return Result.Failure(
                ErrorCodes.ComputerActionFailed,
                "No key or shortcut was named.");
        }

        if (RequiresTargetElement(action.Type) && action.Target is null && !action.HasCoordinates)
        {
            return Result.Failure(
                ErrorCodes.ComputerTargetNotFound,
                "No on-screen element was named for that action.");
        }

        if (action.HasCoordinates
            && safety.RequireApprovalForCoordinateClicks
            && !request.IsConfirmed)
        {
            return Result.Failure(
                ErrorCodes.ComputerCoordinateClickRefused,
                "Clicking by screen coordinates needs your confirmation.");
        }

        return Result.Success();
    }

    public ComputerRiskLevel GetRiskLevel(ComputerActionType actionType) =>
        ComputerActionRisk.GetRiskLevel(actionType);

    private static bool RequiresText(ComputerActionType actionType) =>
        actionType == ComputerActionType.TypeText;

    private static bool RequiresTargetElement(ComputerActionType actionType) => actionType is
        ComputerActionType.Click
        or ComputerActionType.DoubleClick
        or ComputerActionType.RightClick
        or ComputerActionType.Select
        or ComputerActionType.Focus
        or ComputerActionType.ReadUiElement;

    private static bool IsCapabilityEnabled(
        ComputerActionType actionType,
        ComputerSafetyContext safety) => actionType switch
    {
        ComputerActionType.LaunchApplication => safety.AllowApplicationLaunch,
        ComputerActionType.CloseApplication => safety.AllowWindowControl,
        ComputerActionType.SwitchWindow => safety.AllowWindowControl,
        ComputerActionType.MinimizeWindow => safety.AllowWindowControl,
        ComputerActionType.MaximizeWindow => safety.AllowWindowControl,
        ComputerActionType.RestoreWindow => safety.AllowWindowControl,
        ComputerActionType.Focus => safety.AllowWindowControl,
        ComputerActionType.TypeText => safety.AllowKeyboardInput,
        ComputerActionType.PressKey => safety.AllowKeyboardInput,
        ComputerActionType.PressShortcut => safety.AllowKeyboardInput,
        ComputerActionType.Click => safety.AllowMouseInput,
        ComputerActionType.DoubleClick => safety.AllowMouseInput,
        ComputerActionType.RightClick => safety.AllowMouseInput,
        ComputerActionType.Scroll => safety.AllowMouseInput,
        ComputerActionType.Select => safety.AllowMouseInput,
        _ => true
    };
}
