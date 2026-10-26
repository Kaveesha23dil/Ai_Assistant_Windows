using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Application.Agents.Tools;

/// <summary>
/// Operates another application through Windows UI Automation on the person's behalf.
/// <para>
/// Every action goes through <see cref="IComputerControlService"/>, which is the only object
/// allowed to reach the desktop. This tool parses the step into a concrete action and turns the
/// answer back into text; it never calls an input API itself, and it cannot widen the permission
/// the service is about to check.
/// </para>
/// <para>
/// The step is always run in safe mode and is always treated as confirmed. Confirmation is not
/// assumed here: by the time the executor calls a tool that declared an action, the person has
/// answered the approval prompt for that exact step. A tool reached any other way is refused by
/// the service because its own confirmation flag is false.
/// </para>
/// </summary>
public sealed class ComputerControlTool : ITool
{
    private readonly IComputerControlService _control;
    private readonly ILogger<ComputerControlTool> _logger;

    public ComputerControlTool(
        IComputerControlService control,
        ILogger<ComputerControlTool> logger)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(logger);

        _control = control;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "ComputerControlTool";

    /// <inheritdoc />
    public string Description =>
        "Operate a Windows application through its controls. Supply action " +
        "(LaunchApplication, SwitchWindow, Click, DoubleClick, RightClick, TypeText, PressKey, " +
        "PressShortcut, Scroll, Select, Focus, ReadUiElement, FindUiElement, MinimizeWindow, " +
        "MaximizeWindow, RestoreWindow, Wait) and name the target with application, window, " +
        "element, or automationId. Prefer an element name or automation ID over x and y " +
        "coordinates.";

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredInputs => ["action"];

    /// <inheritdoc />
    public PermissionCapability? RequiredPermission => PermissionCapability.ComputerControl;

    /// <inheritdoc />
    public IReadOnlyList<AgentAction> DescribeActions(AgentStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (!TryBuildAction(step.Parameters, out var action) || action is null)
        {
            return [];
        }

        var risk = ComputerActionCatalog.GetRiskLevel(action.Type);

        // A critical action is refused by the service before it runs. Describing it would ask a
        // person to approve something the assistant is never going to do.
        if (risk == ComputerRiskLevel.Critical)
        {
            return [];
        }

        var capability = ComputerActionCatalog.GetCapability(action.Type)
            ?? PermissionCapability.ComputerControl;
        var description = Describe(action);

        return risk switch
        {
            ComputerRiskLevel.ReadOnly => [AgentAction.Read(action.Type.ToString(), description)],
            ComputerRiskLevel.High => [AgentAction.Destructive(action.Type.ToString(), description, capability)],
            _ => [AgentAction.Write(action.Type.ToString(), description, capability)]
        };
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryBuildAction(request.Parameters, out var action) || action is null)
        {
            return ToolResult.Failure(
                Name,
                ErrorCodes.ComputerUnsafeAction,
                "I did not recognise a computer action in that step.");
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await _control.ExecuteAsync(
                new ComputerActionRequest
                {
                    Action = action,
                    Purpose = request.Goal,
                    IsConfirmed = true,
                    IsSafeMode = true
                },
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            if (result.IsFailure || result.Value is null)
            {
                return ToolResult.Failure(
                    Name,
                    result.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                    result.ErrorMessage ?? "I could not complete that computer action.");
            }

            return ToToolResult(action.Type, result.Value, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The action type only. The text of a TypeText action is deliberately absent from
            // every log line, because it may be a password typed into a field.
            _logger.LogError(exception, "Computer action {Action} failed in the agent tool.", action.Type);

            return ToolResult.Failure(
                Name,
                ErrorCodes.ComputerActionFailed,
                "The computer action could not be completed.");
        }
    }

    /// <summary>
    /// Turns the service's result into text the next step can read, keeping any text read from
    /// an element out of the metadata that may be persisted.
    /// </summary>
    private ToolResult ToToolResult(
        ComputerActionType actionType,
        ComputerActionResult result,
        TimeSpan duration)
    {
        var lines = new List<string>();

        if (actionType == ComputerActionType.ReadUiElement
            && result.Data.TryGetValue("text", out var read)
            && !string.IsNullOrWhiteSpace(read))
        {
            lines.Add(read);
        }

        lines.Add(result.Message);

        if (!string.IsNullOrWhiteSpace(result.Verification))
        {
            lines.Add($"Verified: {result.Verification}");
        }

        if (result.ActiveWindow is not null)
        {
            lines.Add($"Active window: {result.ActiveWindow.Label}");
        }

        if (result.Element is not null)
        {
            lines.Add($"Element: {result.Element.Label}");
        }

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["action"] = actionType.ToString(),
            ["succeeded"] = "true"
        };

        foreach (var pair in result.Data)
        {
            // Reading an element can return anything on the screen. It belongs in the answer,
            // which lives for one run, and never in the metadata that outlives it.
            if (string.Equals(pair.Key, "text", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            data[pair.Key] = pair.Value;
        }

        if (result.ActiveWindow is not null)
        {
            data["activeWindow"] = result.ActiveWindow.Label;
        }

        if (result.Element is not null)
        {
            data["element"] = result.Element.Label;
        }

        if (!string.IsNullOrWhiteSpace(result.Verification))
        {
            data["verification"] = result.Verification;
        }

        return ToolResult.Success(
            Name,
            string.Join('\n', lines),
            sources: null,
            data: data,
            duration: duration);
    }

    /// <summary>
    /// Reads a step into one action, or reports that it does not describe one. No partial action
    /// is ever returned: a click with no target would otherwise become a click at whatever the
    /// cursor happened to be over.
    /// </summary>
    private static bool TryBuildAction(
        IReadOnlyDictionary<string, string> parameters,
        out ComputerAction? action)
    {
        action = null;

        if (!parameters.TryGetValue("action", out var actionName)
            || !ComputerActionCatalog.TryParse(actionName, out var actionType))
        {
            return false;
        }

        if (ComputerActionCatalog.GetRiskLevel(actionType) == ComputerRiskLevel.Critical)
        {
            return false;
        }

        var target = ReadTarget(parameters);
        var keys = ReadKeys(parameters);
        var x = ReadInt(parameters, "x");
        var y = ReadInt(parameters, "y");

        if ((x is null) != (y is null))
        {
            return false;
        }

        var timeout = TimeSpan.FromSeconds(
            Math.Clamp(ReadInt(parameters, "timeoutSeconds") ?? 5, 1, 60));

        action = new ComputerAction
        {
            Type = actionType,
            Target = target,
            Text = Read(parameters, "text"),
            Key = Read(parameters, "key"),
            Keys = keys,
            MouseButton = ReadMouseButton(parameters),
            X = target is null ? x : null,
            Y = target is null ? y : null,
            ScrollDelta = ReadInt(parameters, "scrollDelta") ?? 0,
            Timeout = timeout,
            VerifyResult = ReadBool(parameters, "verify") ?? true,
            MaxRetries = 0,
            UseCoordinateFallback = ReadBool(parameters, "useCoordinateFallback") ?? false
        };

        return true;
    }

    private static ComputerTarget? ReadTarget(IReadOnlyDictionary<string, string> parameters)
    {
        var application = Read(parameters, "application");
        if (application is not null)
        {
            return ComputerTarget.Application(application);
        }

        var window = Read(parameters, "window") ?? Read(parameters, "windowTitle");
        var process = Read(parameters, "processName");
        var element = Read(parameters, "element") ?? Read(parameters, "elementName");
        var automationId = Read(parameters, "automationId");
        var controlType = Read(parameters, "controlType");
        var className = Read(parameters, "className");

        if (element is not null || automationId is not null
            || controlType is not null || className is not null)
        {
            // The window is kept even when an element is named: it narrows the search, and the
            // service refuses the action if a different window is in the foreground.
            return new ComputerTarget
            {
                Kind = ComputerTargetKind.Element,
                WindowTitle = window,
                ProcessName = process,
                ElementName = element,
                AutomationId = automationId,
                ControlType = controlType,
                ClassName = className
            };
        }

        if (window is not null || process is not null)
        {
            return new ComputerTarget
            {
                Kind = ComputerTargetKind.Window,
                WindowTitle = window,
                ProcessName = process
            };
        }

        return null;
    }

    private static IReadOnlyList<string> ReadKeys(IReadOnlyDictionary<string, string> parameters)
    {
        var raw = Read(parameters, "keys");

        if (raw is null)
        {
            var single = Read(parameters, "key");
            return single is null ? [] : [single];
        }

        return raw
            .Split([',', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static MouseButtonType ReadMouseButton(IReadOnlyDictionary<string, string> parameters) =>
        Enum.TryParse<MouseButtonType>(Read(parameters, "button"), ignoreCase: true, out var button)
            ? button
            : MouseButtonType.Left;

    private static string Describe(ComputerAction action)
    {
        var target = action.Target;
        var element = target?.ElementName ?? target?.AutomationId ?? "the chosen element";
        var window = target?.WindowTitle ?? target?.ProcessName ?? target?.ApplicationName ?? "the window";

        return action.Type switch
        {
            ComputerActionType.LaunchApplication =>
                $"Open {target?.ApplicationName ?? action.Text ?? "an application"}.",
            ComputerActionType.CloseApplication => $"Close {window}.",
            ComputerActionType.SwitchWindow => $"Switch to {window}.",
            ComputerActionType.MinimizeWindow => $"Minimize {window}.",
            ComputerActionType.MaximizeWindow => $"Maximize {window}.",
            ComputerActionType.RestoreWindow => $"Restore {window}.",
            ComputerActionType.Click => $"Click {element}.",
            ComputerActionType.DoubleClick => $"Double-click {element}.",
            ComputerActionType.RightClick => $"Right-click {element}.",
            ComputerActionType.TypeText =>
                $"Type {(action.Text?.Length ?? 0).ToString(CultureInfo.InvariantCulture)} " +
                $"characters into {element}.",
            ComputerActionType.PressKey or ComputerActionType.PressShortcut =>
                $"Press {string.Join("+", action.Keys.Count > 0 ? action.Keys : (IReadOnlyList<string>)[action.Key ?? "a key"])}.",
            ComputerActionType.Scroll => "Scroll the active window.",
            ComputerActionType.Select => $"Select {element}.",
            ComputerActionType.Focus => $"Focus {element}.",
            ComputerActionType.ReadUiElement => $"Read the text of {element}.",
            ComputerActionType.FindUiElement => $"Find {element}.",
            ComputerActionType.Wait => $"Wait {action.Timeout.TotalSeconds:0.#} seconds.",
            _ => $"Run {action.Type}."
        };
    }

    private static string? Read(IReadOnlyDictionary<string, string> parameters, string name) =>
        parameters.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static int? ReadInt(IReadOnlyDictionary<string, string> parameters, string name) =>
        int.TryParse(Read(parameters, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static bool? ReadBool(IReadOnlyDictionary<string, string> parameters, string name) =>
        bool.TryParse(Read(parameters, name), out var value) ? value : null;
}
