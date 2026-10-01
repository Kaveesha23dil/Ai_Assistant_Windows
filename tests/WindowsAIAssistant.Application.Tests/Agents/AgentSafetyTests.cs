using WindowsAIAssistant.Application.Agents;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Tests.Agents;

/// <summary>
/// Covers the rules that stand between a plan and a change to the machine.
/// <para>
/// Every case here asserts a refusal or a stop, not only the successful path. A suite that proved
/// only that a well-behaved plan works would keep passing after a change let an ill-behaved one
/// through, and the ill-behaved one is the whole reason these rules exist.
/// </para>
/// </summary>
public sealed class AgentSafetyTests
{
    [Fact]
    public async Task AReadOnlyToolIsNeverHeldAtTheGate()
    {
        var approvals = new RecordingApprovalGate();
        var executor = Executor(approvals, new SearchTool());

        var result = await executor.ExecuteAsync(
            AgentPlan
                .Single("Find a file", "SearchTool", "Search", "find a file", parameters: Parameters(("query", "report")))
                .AsReady());

        Assert.True(result.IsSuccess);
        Assert.Empty(approvals.Requests);
    }

    [Fact]
    public async Task AWriteToolIsHeldAndRefusalLeavesNothingWritten()
    {
        var approvals = new RecordingApprovalGate
        {
            Decide = request => AgentApprovalDecision.Reject(request.Id, "no"),
        };

        var tool = new WriteTool();
        var executor = Executor(approvals, tool);

        var result = await executor.ExecuteAsync(
            AgentPlan.Single("Write a report", tool.Name, "Write", "write a report").AsReady());

        Assert.True(result.IsSuccess);
        Assert.Single(approvals.Requests);
        Assert.Equal(AgentPlanStatus.Rejected, result.Value!.Status);

        // The assertion that matters: the tool was never called, so there is nothing to undo.
        Assert.False(tool.WasCalled);
    }

    [Fact]
    public async Task AModificationTheToolCannotApplyStopsTheRun()
    {
        var approvals = new RecordingApprovalGate
        {
            Decide = request => AgentApprovalDecision.Modify(request.Id, "somewhere else entirely"),
        };

        var tool = new WriteTool { AcceptsModification = false };
        var executor = Executor(approvals, tool);

        var result = await executor.ExecuteAsync(
            AgentPlan.Single("Write a report", tool.Name, "Write", "write a report").AsReady());

        // Before this was fixed, the failure was assigned to the pending step and the tool ran
        // anyway: a person who changed the details of a request got the original request done.
        Assert.True(result.IsSuccess);
        Assert.Equal(AgentPlanStatus.Failed, result.Value!.Status);
        Assert.False(tool.WasCalled);
    }

    [Fact]
    public async Task AModificationTheToolAcceptsIsAppliedBeforeTheToolRuns()
    {
        var approvals = new RecordingApprovalGate
        {
            Decide = request => AgentApprovalDecision.Modify(request.Id, "weekly-report.md"),
        };

        var tool = new WriteTool { AcceptsModification = true };
        var executor = Executor(approvals, tool);

        await executor.ExecuteAsync(
            AgentPlan.Single("Write a report", tool.Name, "Write", "write a report").AsReady());

        Assert.True(tool.WasCalled);
        Assert.Equal("weekly-report.md", tool.SeenFileName);
    }

    [Fact]
    public async Task AWriteToolWithItsSwitchOffIsRefusedBeforeTheRunStarts()
    {
        var approvals = new RecordingApprovalGate();
        var tool = new WriteTool { RequiredPermission = PermissionCapability.FileWrite };
        var permissions = new FakePermissionService();
        permissions.Denied.Add(PermissionCapability.FileWrite);
        var executor = Executor(approvals, [tool], permissions);

        var result = await executor.ExecuteAsync(
            AgentPlan.Single("Write a report", tool.Name, "Write", "write a report").AsReady());

        // Refused at validation, before the run begins, rather than as a failed step part-way
        // through: the stronger of the two, and the one worth pinning down in a test. Nothing
        // was offered to the gate and nothing was written.
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.AgentToolUnavailable, result.ErrorCode);
        Assert.Empty(approvals.Requests);
        Assert.False(tool.WasCalled);
    }

    [Fact]
    public async Task APlanThatWasNeverCheckedIsRefusedRatherThanRun()
    {
        var tool = new SearchTool();
        var executor = Executor(new RecordingApprovalGate(), tool);

        var draft = AgentPlan.Single("Find a file", tool.Name, "Search", "find a file");

        var result = await executor.ExecuteAsync(draft);

        Assert.True(result.IsFailure);
        Assert.False(tool.WasCalled);
    }
    [Fact]
    public async Task APlanNamingAToolNobodyRegisteredIsRefused()
    {
        var executor = Executor(new RecordingApprovalGate(), new SearchTool());

        var plan = AgentPlan
            .Single("Do something", "NoSuchTool", "Do it", "do something")
            .AsReady();

        var result = await executor.ExecuteAsync(plan);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.AgentToolUnknown, result.ErrorCode);
    }

    [Fact]
    public async Task ASearchPassesWhatItFoundToTheStepThatReadsIt()
    {
        var search = new SearchTool { Content = "the meeting is on Tuesday" };
        var answer = new ReadingTool();
        var executor = Executor(
            new RecordingApprovalGate(),
            search,
            answer);

        var plan = AgentPlan.Create(
            "Answer from the documents",
            [AgentStep.Create(search.Name, "Search", parameters: new Dictionary<string, string> { ["query"] = "when" }),
             AgentStep.Create(answer.Name, "Answer")],
            "when is the meeting");

        var result = await executor.ExecuteAsync(plan.AsReady());

        Assert.True(result.IsSuccess);

        // Neither tool knows the other exists; the handoff is the run's only channel between
        // them, and if it regressed the answer tool would silently see an empty context.
        Assert.Equal("the meeting is on Tuesday", answer.SeenContext);
    }

    [Fact]
    public async Task TheFirstStepIsGivenNoContextFromAnEarlierRun()
    {
        var answer = new ReadingTool();
        var executor = Executor(new RecordingApprovalGate(), new SearchTool(), answer);

        var first = AgentPlan
            .Single("Search", "SearchTool", "Search", "when", parameters: Parameters(("query", "when")))
            .AsReady();

        await executor.ExecuteAsync(first);

        var second = AgentPlan
            .Single("Answer", answer.Name, "Answer", "when is it")
            .AsReady();

        await executor.ExecuteAsync(second);

        // A run cannot read a previous run's documents. Each run builds its own context, and the
        // only thing carried between them is what a person was told.
        Assert.Null(answer.SeenContext);
    }

    private static IReadOnlyDictionary<string, string> Parameters((string Key, string Value) pair) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [pair.Key] = pair.Value };

    private static AgentExecutor Executor(
        RecordingApprovalGate approvals,
        params ITool[] tools) =>
        Executor(approvals, tools, new FakePermissionService());

    private static AgentExecutor Executor(
        RecordingApprovalGate approvals,
        ITool[] tools,
        IPermissionService permissions) =>
        new(
            new FakeToolRegistry(tools, permissions),
            approvals,
            permissions,
            new TestLogger<AgentExecutor>());

    /// <summary>A tool that only reads.</summary>
    private sealed class SearchTool : ITool
    {
        public string Name => "SearchTool";

        public string Description => "Searches.";

        public IReadOnlyList<string> RequiredInputs => ["query"];

        public IReadOnlyList<AgentAction> DescribeActions(AgentStep step) => [];

        public string Content { get; set; } = "something found";

        public bool WasCalled { get; private set; }

        public Task<ToolResult> ExecuteAsync(ToolRequest request, CancellationToken cancellationToken = default)
        {
            WasCalled = true;

            return Task.FromResult(ToolResult.Success(
                Name,
                Content,
                ["a-source.txt"],
                ToolResult.Data1("found", 1)));
        }
    }

    /// <summary>A tool that reads what earlier steps found.</summary>
    private sealed class ReadingTool : ITool
    {
        public string Name => "ReadingTool";

        public string Description => "Reads.";

        public IReadOnlyList<string> RequiredInputs => [];

        public IReadOnlyList<AgentAction> DescribeActions(AgentStep step) => [];

        public string? SeenContext { get; private set; }

        public Task<ToolResult> ExecuteAsync(ToolRequest request, CancellationToken cancellationToken = default)
        {
            SeenContext = request.GetMaterial();
            return Task.FromResult(ToolResult.Success(Name, "an answer"));
        }
    }

    /// <summary>A tool that writes a file, and says so.</summary>
    private sealed class WriteTool : ITool
    {
        public string Name => "WriteTool";

        public string Description => "Writes a file.";

        public IReadOnlyList<string> RequiredInputs => [];

        public PermissionCapability? RequiredPermission { get; set; } = PermissionCapability.FileWrite;

        public bool AcceptsModification { get; set; }

        public bool WasCalled { get; private set; }

        public string? SeenFileName { get; private set; }

        public IReadOnlyList<AgentAction> DescribeActions(AgentStep step) =>
            [AgentAction.Write(
                "Write a file",
                $"Write {step.Parameters.GetValueOrDefault("fileName", "a file")}.",
                PermissionCapability.FileWrite)];

        public bool TryApplyModification(AgentStep step, string modification, out AgentStep modified)
        {
            modified = step with
            {
                Parameters = new Dictionary<string, string>(step.Parameters, StringComparer.OrdinalIgnoreCase)
                {
                    ["fileName"] = modification,
                },
            };

            return AcceptsModification;
        }

        public Task<ToolResult> ExecuteAsync(ToolRequest request, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            SeenFileName = request.GetParameter("fileName");
            return Task.FromResult(ToolResult.Success(Name, "written"));
        }
    }
}
