using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Application.Agents.Tools;
using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;
using WindowsAIAssistant.Core.Models.Computer;
using WindowsAIAssistant.Infrastructure.Computer;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Application.Tests.Computer;

/// <summary>
/// The safety rules a computer action passes through before anything on the desktop changes.
/// These cases touch no window and open no application: the validator, the permission check,
/// the tool, and the workflow executor are driven through fakes.
/// </summary>
public sealed class ComputerControlTests
{
    [Fact]
    public void AComputerActionIsRefusedWhenTheFeatureIsOff()
    {
        var validator = new ComputerActionValidator();
        var request = Request(ComputerActionType.FindUiElement);

        var result = validator.Validate(
            request,
            new ComputerSafetyContext { IsComputerControlAllowed = false });

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ComputerControlDisabled, result.ErrorCode);
    }

    [Fact]
    public void SafeModeRefusesATypeActionEvenWhenTheSwitchIsOn()
    {
        var validator = new ComputerActionValidator();
        var request = Request(ComputerActionType.TypeText, text: "hello");

        var result = validator.Validate(
            request,
            new ComputerSafetyContext
            {
                IsComputerControlAllowed = true,
                AllowKeyboardInput = true,
                SafeMode = true
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ComputerUnsafeAction, result.ErrorCode);
    }

    [Fact]
    public void AnAskEveryTimeActionNeedsConfirmation()
    {
        var permissions = new ComputerPermissionService(
            new StaticOptionsMonitor<ComputerControlOptions>(new ComputerControlOptions
            {
                AllowComputerControl = true,
                AllowMouseInput = true,
                Permissions = new Dictionary<string, ComputerPermissionLevel>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["Click"] = ComputerPermissionLevel.AskEveryTime
                }
            }),
            NullLogger<ComputerPermissionService>.Instance);

        var unconfirmed = permissions.EnsureAllowed(ComputerActionType.Click, isConfirmed: false);
        var confirmed = permissions.EnsureAllowed(ComputerActionType.Click, isConfirmed: true);

        Assert.False(unconfirmed.IsSuccess);
        Assert.Equal(ErrorCodes.ComputerApprovalRequired, unconfirmed.ErrorCode);
        Assert.True(confirmed.IsSuccess);
    }

    [Fact]
    public void ATrustedNameDoesNotAdmitEveryProgramThatContainsIt()
    {
        var permissions = new ComputerPermissionService(
            new StaticOptionsMonitor<ComputerControlOptions>(new ComputerControlOptions
            {
                AllowComputerControl = true,
                AllowApplicationLaunch = true,
                TrustedApplications = ["word"]
            }),
            NullLogger<ComputerPermissionService>.Instance);

        Assert.True(permissions.EnsureApplicationAllowed("word").IsSuccess);
        Assert.True(permissions.EnsureApplicationAllowed("word.exe").IsSuccess);

        var impostor = permissions.EnsureApplicationAllowed("password-stealer.exe");
        Assert.False(impostor.IsSuccess);
        Assert.Equal(ErrorCodes.ComputerPermissionDenied, impostor.ErrorCode);
    }

    [Fact]
    public void TheToolDescribesTheCharactersItWillTypeWithoutRepeatingThem()
    {
        using var control = new FakeComputerControl();
        var tool = new ComputerControlTool(control, NullLogger<ComputerControlTool>.Instance);

        var actions = tool.DescribeActions(Step(
            "TypeText",
            ("action", "TypeText"),
            ("element", "Password"),
            ("text", "correct horse battery staple")));

        var action = Assert.Single(actions);
        Assert.Equal(ActionRiskLevel.Medium, action.RiskLevel);
        Assert.True(action.RequiresApproval);
        Assert.DoesNotContain("correct horse", action.Description, StringComparison.Ordinal);
        Assert.Contains("28 characters", action.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheToolRunsConfirmedAndInSafeMode()
    {
        using var control = new FakeComputerControl
        {
            Result = ComputerActionResult.Success(
                ComputerActionType.FindUiElement,
                "Found Save.",
                element: new UiElementInfo { Id = "save", Name = "Save" })
        };

        var tool = new ComputerControlTool(control, NullLogger<ComputerControlTool>.Instance);

        var result = await tool.ExecuteAsync(new ToolRequest(
            tool.Name,
            "Find the Save button",
            "find the save button",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = "FindUiElement",
                ["element"] = "Save",
                ["window"] = "Notepad"
            }));

        Assert.True(result.IsSuccess);
        var request = Assert.Single(control.Requests);
        Assert.True(request.IsConfirmed);
        Assert.True(request.IsSafeMode);
        Assert.Equal(ComputerActionType.FindUiElement, request.Action.Type);
        Assert.Equal("Save", request.Action.Target?.ElementName);
        Assert.Equal("Notepad", request.Action.Target?.WindowTitle);
    }

    [Fact]
    public async Task TextReadFromAnElementStaysOutOfStoredMetadata()
    {
        using var control = new FakeComputerControl
        {
            Result = ComputerActionResult.Success(
                ComputerActionType.ReadUiElement,
                "Read 7 characters.",
                element: new UiElementInfo { Id = "field", Name = "Password" },
                data: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["text"] = "hunter2",
                    ["length"] = "7"
                })
        };

        var tool = new ComputerControlTool(control, NullLogger<ComputerControlTool>.Instance);

        var result = await tool.ExecuteAsync(new ToolRequest(
            tool.Name,
            "Read the password field",
            "read the password field",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = "ReadUiElement",
                ["element"] = "Password"
            }));

        Assert.True(result.IsSuccess);
        Assert.Contains("hunter2", result.Content, StringComparison.Ordinal);
        Assert.False(result.Data.ContainsKey("text"));
        Assert.Equal("7", result.Data["length"]);
    }

    [Fact]
    public async Task AnUnknownActionIsRefusedRatherThanGuessed()
    {
        using var control = new FakeComputerControl();
        var tool = new ComputerControlTool(control, NullLogger<ComputerControlTool>.Instance);

        var result = await tool.ExecuteAsync(new ToolRequest(
            tool.Name,
            "Do something",
            "do something",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = "ReformatTheHardDrive"
            }));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ComputerUnsafeAction, result.ErrorCode);
        Assert.Empty(control.Requests);
    }

    [Fact]
    public async Task AWorkflowStopsAtTheStepThatFails()
    {
        using var control = new FakeComputerControl
        {
            OnExecute = request => request.Action.Type == ComputerActionType.Wait
                ? Result<ComputerActionResult>.Failure(
                    ErrorCodes.ComputerActionFailed,
                    "That step failed on purpose.")
                : Result<ComputerActionResult>.Success(ComputerActionResult.Success(
                    request.Action.Type,
                    "Done."))
        };

        var executor = new ComputerWorkflowExecutor(
            control,
            new FakeComputerContext(),
            NullLogger<ComputerWorkflowExecutor>.Instance);

        var workflow = new ComputerWorkflow
        {
            Name = "Two good steps and a bad one",
            IsConfirmed = true,
            Steps =
            [
                new ComputerWorkflowStep { Action = new ComputerAction { Type = ComputerActionType.Scroll } },
                new ComputerWorkflowStep { Action = new ComputerAction { Type = ComputerActionType.Scroll } },
                new ComputerWorkflowStep { Action = new ComputerAction { Type = ComputerActionType.Wait } }
            ]
        };

        var result = await executor.ExecuteAsync(workflow);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.Value);
        Assert.False(result.Value!.Succeeded);
        Assert.Equal(3, result.Value.FailedStep);
        Assert.Equal(2, result.Value.CompletedSteps);
        Assert.Equal(3, control.Requests.Count);
    }

    [Fact]
    public async Task AWorkflowReportsAnElementThatWasNotTheExpectedOne()
    {
        using var control = new FakeComputerControl
        {
            Result = ComputerActionResult.Success(
                ComputerActionType.FindUiElement,
                "Found Cancel.",
                element: new UiElementInfo { Id = "cancel", Name = "Cancel" })
        };

        var executor = new ComputerWorkflowExecutor(
            control,
            new FakeComputerContext(),
            NullLogger<ComputerWorkflowExecutor>.Instance);

        var workflow = new ComputerWorkflow
        {
            Name = "Find the Save button",
            IsConfirmed = true,
            Steps =
            [
                new ComputerWorkflowStep
                {
                    Action = new ComputerAction
                    {
                        Type = ComputerActionType.FindUiElement,
                        Target = ComputerTarget.Element("Save")
                    },
                    ExpectedElementName = "Save"
                }
            ]
        };

        var result = await executor.ExecuteAsync(workflow);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(result.Value!.Succeeded);
        Assert.Equal(1, result.Value.FailedStep);
    }

    private static ComputerActionRequest Request(ComputerActionType type, string? text = null) =>
        new()
        {
            Action = new ComputerAction
            {
                Type = type,
                Text = text,
                Target = ComputerTarget.Element("Save")
            }
        };

    private static AgentStep Step(
        string description,
        params (string Name, string Value)[] parameters) =>
        AgentStep.Create(
            "ComputerControlTool",
            description,
            "A step, for the tool to describe.",
            parameters.ToDictionary(
                pair => pair.Name,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase));

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class FakeComputerControl : IComputerControlService, IDisposable
    {
        public List<ComputerActionRequest> Requests { get; } = [];

        public ComputerActionResult? Result { get; init; }

        public Func<ComputerActionRequest, Result<ComputerActionResult>>? OnExecute { get; init; }

        public bool IsBusy => false;

        public Task<Result<ComputerActionResult>> ExecuteAsync(
            ComputerActionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (OnExecute is not null)
            {
                return Task.FromResult(OnExecute(request));
            }

            return Task.FromResult(Result is null
                ? Result<ComputerActionResult>.Failure(
                    ErrorCodes.ComputerActionFailed,
                    "No result was configured.")
                : Result<ComputerActionResult>.Success(Result));
        }

        public Task<Result<ComputerContext>> GetContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ComputerContext>.Success(new ComputerContext()));

        public void CancelCurrent()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeComputerContext : IComputerContextService
    {
        public Task<Result<ComputerContext>> GetContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ComputerContext>.Success(new ComputerContext()));
    }
}
