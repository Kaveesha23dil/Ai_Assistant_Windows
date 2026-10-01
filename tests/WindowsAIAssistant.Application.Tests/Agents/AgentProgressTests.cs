using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Tests.Agents;

/// <summary>
/// Covers the reporting a workspace depends on.
/// <para>
/// The workspace draws the plan, lists the steps with their outcomes, and shows the approval
/// from what arrives on this one event. That is only possible if each report carries the thing
/// it is about, so these tests pin down what is present. The other half of the job is just as
/// important and is asserted here too: a report is the most convenient place in the system for a
/// document's text to escape into, because it is already on its way to the screen, so the
/// reports must be built from descriptions and counts and never from what a step read.
/// </para>
/// </summary>
public sealed class AgentProgressTests
{
    [Fact]
    public void APlanIsAnnouncedBeforeAnyStepBegins()
    {
        // Without this the workspace has nothing to draw its step list from until the first step
        // starts, and a run that is refused at the gate shows no plan at all.
        var plan = AgentPlan.Single("Answer", "SearchTool", "Search", "what does it say");

        var progress = AgentProgress.PlanReady(Guid.NewGuid(), plan, 1);

        Assert.Equal(AgentProgressKind.PlanReady, progress.Kind);
        Assert.Same(plan, progress.Plan);
        Assert.Equal(1, progress.TotalSteps);
        Assert.Equal(0, progress.CompletedSteps);
        Assert.Null(progress.Step);
    }

    [Fact]
    public void AnApprovalIsCarriedWithTheReportThatAnnouncesIt()
    {
        // The interface has to be able to show the prompt and answer it. It cannot fetch the
        // pending request separately, because by the time it could the person is looking at a
        // prompt with no explanation on it.
        var runId = Guid.NewGuid();
        var step = AgentStep.Create("WriteTool", "Write the report");
        var action = AgentAction.Write("Write the report", "Writes report.md.", PermissionCapability.FileWrite);
        var approval = AgentApprovalRequest.For(runId, step, action);

        var progress = AgentProgress.AwaitingApproval(runId, step, approval, 0, 1);

        Assert.Equal(AgentProgressKind.AwaitingApproval, progress.Kind);
        Assert.Same(approval, progress.Approval);
        Assert.Equal(approval.Id, progress.Approval!.Id);
        Assert.Equal(AgentStepStatus.AwaitingApproval, progress.Status);
        Assert.Equal(approval.Description, progress.Message);
    }

    [Fact]
    public void AFinishedStepReportsTheToolsOwnSummaryRatherThanItsContent()
    {
        // The summary is rebuilt by the step from the result's counts, not taken from the
        // content. That is the whole point: the summary is what goes on the screen, so a tool
        // that read a document cannot put a line of it there by wording its result well.
        const string content = "The quarterly figures for the north region were not supplied.";

        var step = AgentStep
            .Create("WriteTool", "Write the report")
            .AsSucceeded(ToolResult.Success(
                "WriteTool",
                content,
                ["report.md"],
                new Dictionary<string, string> { ["body"] = content }));

        var progress = AgentProgress.Finished(Guid.NewGuid(), step, 1, 1);

        Assert.NotNull(progress.Message);
        Assert.Equal(step.ResultSummary, progress.Message);
        Assert.DoesNotContain("north region", progress.Message!, StringComparison.Ordinal);
        Assert.DoesNotContain("north region", progress.Step!.ResultSummary ?? string.Empty, StringComparison.Ordinal);

        // The content is still on the step for the next step in the run to read. The point is
        // that the report does not carry it, not that the run throws it away.
        Assert.Equal(content, step.Result!.Content);
        Assert.Null(progress.Plan);
    }

    [Fact]
    public void AFinishedStepStillReportsWhichStepItWasAbout()
    {
        var step = AgentStep
            .Create("SearchTool", "Search the knowledge base")
            .AsSucceeded(ToolResult.Success("SearchTool", "the answer is 42"));

        var progress = AgentProgress.Finished(Guid.NewGuid(), step, 1, 1);

        Assert.Equal(AgentProgressKind.StepFinished, progress.Kind);
        Assert.Same(step, progress.Step);
        Assert.Equal("SearchTool", progress.ToolName);
        Assert.Equal(AgentStepStatus.Succeeded, progress.Status);
    }

    [Fact]
    public void AFailedStepReportsTheReasonRatherThanItsInput()
    {
        // An error message is shown to the person, so it is a place a provider or a tool could
        // quote their words back at them. The assertion is that the progress carries the message
        // this code wrote, and that the step's parameters — which for a document tool is the
        // document's path and for a search tool is the person's question — stay on the step.
        var step = AgentStep
            .Create("SearchTool", "Search")
            .AsFailed(ErrorCodes.AgentToolUnavailable, "The search index is not available.");

        var progress = AgentProgress.Finished(Guid.NewGuid(), step, 0, 1);

        Assert.Equal("The search index is not available.", progress.Message);
        Assert.Equal(ErrorCodes.AgentToolUnavailable, progress.Step!.ErrorCode);
    }

    [Fact]
    public void TheEndOfARunIsReportedOnceAndCarriesTheFinalPlan()
    {
        var plan = AgentPlan.Single("Answer", "SearchTool", "Search", "what does it say");
        var runId = Guid.NewGuid();

        var completed = AgentProgress.Completed(runId, plan, AgentPlanStatus.Completed);
        var stopped = AgentProgress.Completed(runId, plan, AgentPlanStatus.Cancelled);

        Assert.True(completed.IsFinished);
        Assert.True(stopped.IsFinished);
        Assert.Same(plan, completed.Plan);
        Assert.Equal(AgentStepStatus.Succeeded, completed.Status);
        Assert.Equal(AgentStepStatus.Failed, stopped.Status);
    }

    [Fact]
    public void EveryReportNamesTheRunItBelongsTo()
    {
        // The workspace is a singleton serving one page, and the progress event is raised from
        // the agent. Without the run on every report, two runs interleaved could not be told
        // apart and one run's steps would appear under the other.
        var runId = Guid.NewGuid();
        var plan = AgentPlan.Single("Answer", "SearchTool", "Search", "what does it say");
        var step = AgentStep.Create("SearchTool", "Search");

        var reports = new[]
        {
            AgentProgress.PlanReady(runId, plan, 1),
            AgentProgress.Started(runId, step, 0, 1),
            AgentProgress.Finished(runId, step.AsSucceeded(ToolResult.Success("SearchTool", "found")), 1, 1),
            AgentProgress.Completed(runId, plan, AgentPlanStatus.Completed),
        };

        Assert.All(reports, report => Assert.Equal(runId, report.RunId));
    }

    [Fact]
    public void OnlyTheEndOfARunClaimsToBeFinished()
    {
        // "Finished" drives whether a caller waits or stops waiting. A step finishing is not a
        // run finishing, and treating it as one would leave a run with nothing pending forever.
        var plan = AgentPlan.Single("Answer", "SearchTool", "Search", "what does it say");
        var step = AgentStep.Create("SearchTool", "Search");

        Assert.False(AgentProgress.PlanReady(Guid.NewGuid(), plan, 1).IsFinished);
        Assert.False(AgentProgress.Started(Guid.NewGuid(), step, 0, 1).IsFinished);
        Assert.False(AgentProgress
            .Finished(Guid.NewGuid(), step.AsSucceeded(ToolResult.Success("SearchTool", "found")), 1, 1)
            .IsFinished);
        Assert.True(AgentProgress.Completed(Guid.NewGuid(), plan, AgentPlanStatus.Completed).IsFinished);
    }
}
