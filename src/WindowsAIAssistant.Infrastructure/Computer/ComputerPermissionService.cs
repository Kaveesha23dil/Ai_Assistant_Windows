using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Computer;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Computer;

public sealed class ComputerPermissionService : IComputerPermissionService
{
    private readonly IOptionsMonitor<ComputerControlOptions> _options;
    private readonly ILogger<ComputerPermissionService> _logger;

    public ComputerPermissionService(
        IOptionsMonitor<ComputerControlOptions> options,
        ILogger<ComputerPermissionService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    public ComputerPermission GetPermission(ComputerActionType actionType)
    {
        var options = _options.CurrentValue;
        var risk = ComputerActionRisk.GetRiskLevel(actionType);
        var capability = ComputerActionRisk.GetCapability(actionType);

        if (!options.AllowComputerControl || !IsCapabilityEnabled(options, capability))
        {
            return new ComputerPermission
            {
                ActionType = actionType,
                Level = ComputerPermissionLevel.Blocked,
                RiskLevel = risk,
                Capability = capability
            };
        }

        var level = ReadConfiguredLevel(options, actionType);
        if (level is null)
        {
            level = risk == ComputerRiskLevel.ReadOnly
                ? ComputerPermissionLevel.AlwaysAllow
                : ComputerPermissionLevel.AskEveryTime;
        }

        return new ComputerPermission
        {
            ActionType = actionType,
            Level = level.Value,
            RiskLevel = risk,
            Capability = capability
        };
    }

    public ComputerPermissionLevel GetLevel(PermissionCapability capability)
    {
        var options = _options.CurrentValue;
        if (!options.AllowComputerControl)
        {
            return ComputerPermissionLevel.Blocked;
        }

        if (!IsCapabilityEnabled(options, capability))
        {
            return ComputerPermissionLevel.Blocked;
        }

        var action = ActionForCapability(capability);
        return action is null
            ? ComputerPermissionLevel.AskEveryTime
            : GetPermission(action.Value).Level;
    }

    public Result EnsureAllowed(ComputerActionType actionType, bool isConfirmed)
    {
        var permission = GetPermission(actionType);

        if (permission.Level == ComputerPermissionLevel.Blocked)
        {
            _logger.LogInformation(
                "Computer action {Action} was refused by the current permission settings.",
                actionType);

            return permission.RiskLevel == ComputerRiskLevel.Critical
                ? Result.Failure(
                    ErrorCodes.ComputerUnsafeAction,
                    "That computer action is destructive and is not available.")
                : Result.Failure(
                    ErrorCodes.ComputerPermissionDenied,
                    "Computer control, or the permission this action needs, is turned off in Settings.");
        }

        if (permission.RiskLevel == ComputerRiskLevel.Critical)
        {
            return Result.Failure(
                ErrorCodes.ComputerUnsafeAction,
                "That computer action is destructive and is not available.");
        }

        if (permission.Level == ComputerPermissionLevel.AskEveryTime && !isConfirmed)
        {
            return Result.Failure(
                ErrorCodes.ComputerApprovalRequired,
                "That computer action needs your confirmation before it can run.");
        }

        return Result.Success();
    }

    public Result EnsureApplicationAllowed(string applicationName)
    {
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            return Result.Failure(ErrorCodes.ComputerTargetNotFound, "No application was named.");
        }

        var options = _options.CurrentValue;
        var name = applicationName.Trim();

        if (options.BlockedApplications.Any(blocked =>
                IsSameApplication(name, blocked)
                || name.Contains(blocked, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure(
                ErrorCodes.ComputerPermissionDenied,
                $"{name} is on the blocked application list.");
        }

        if (options.TrustedApplications.Length > 0
            && !options.TrustedApplications.Any(trusted => IsSameApplication(name, trusted)))
        {
            return Result.Failure(
                ErrorCodes.ComputerPermissionDenied,
                $"{name} is not on the trusted application list.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Compares names as the same application when they name the same file, not when one name
    /// happens to appear inside the other. A trusted "word" must not admit a program called
    /// "password-stealer.exe"; a block list is deliberately broader and is handled by the caller.
    /// </summary>
    private static bool IsSameApplication(string candidate, string configured)
    {
        if (candidate.Equals(configured, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var candidateFile = Path.GetFileName(candidate);
        var configuredFile = Path.GetFileName(configured);

        return candidateFile.Equals(configuredFile, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileNameWithoutExtension(candidateFile)
                .Equals(Path.GetFileNameWithoutExtension(configuredFile), StringComparison.OrdinalIgnoreCase);
    }

    private static ComputerPermissionLevel? ReadConfiguredLevel(
        ComputerControlOptions options,
        ComputerActionType actionType)
    {
        if (options.Permissions.TryGetValue(actionType.ToString(), out var level))
        {
            return level;
        }

        foreach (var pair in options.Permissions)
        {
            if (Enum.TryParse<ComputerActionType>(pair.Key, ignoreCase: true, out var parsed)
                && parsed == actionType)
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static bool IsCapabilityEnabled(
        ComputerControlOptions options,
        PermissionCapability? capability) => capability switch
    {
        PermissionCapability.ComputerApplicationLaunch => options.AllowApplicationLaunch,
        PermissionCapability.ComputerWindowControl => options.AllowWindowControl,
        PermissionCapability.ComputerKeyboardInput => options.AllowKeyboardInput,
        PermissionCapability.ComputerMouseInput => options.AllowMouseInput,
        PermissionCapability.ComputerFormSubmission => options.AllowFormSubmission,
        PermissionCapability.ComputerControl => true,
        null => true,
        _ => false
    };

    private static ComputerActionType? ActionForCapability(PermissionCapability capability) => capability switch
    {
        PermissionCapability.ComputerApplicationLaunch => ComputerActionType.LaunchApplication,
        PermissionCapability.ComputerWindowControl => ComputerActionType.SwitchWindow,
        PermissionCapability.ComputerKeyboardInput => ComputerActionType.TypeText,
        PermissionCapability.ComputerMouseInput => ComputerActionType.Click,
        PermissionCapability.ComputerFormSubmission => ComputerActionType.Click,
        _ => null
    };
}
