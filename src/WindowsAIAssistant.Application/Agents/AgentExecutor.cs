using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents;

/// <summary>
/// Checks a plan against the tools that exist, runs it a step at a time, and holds each step at
/// an approval gate when the step would change something.
/// <para>
/// Every step is checked before it runs — that the tool is registered, that it is available on
/// this machine, that it is being given what it requires, and that the consent switch it
/// depends on is on. The checks are repeated here even though the planner already made them,
/// because a plan can be built by hand, restored from somewhere, or written by a test, and this
/// is the last point at which those cases can be caught. The planner's checks make a bad plan
/// unlikely; these make it harmless.
/// </para>
/// <para>
/// The context handed between steps is the only channel: a step reads what earlier steps left
/// under a key both of them can name, and never reaches into a tool directly. That is what makes
/// "search, then answer" work without either step knowing the other exists.
/// </para>
/// </summary>
public sealed class AgentExecutor : IAgentExecutor
{
    /// <summary>
    /// How long a prompt waits for an answer by default. Long enough to read a sentence and
    /// decide, short enough that a demonstration cannot be left sitting on a dialog.
    /// </summary>
    private static readonly TimeSpan DefaultApprovalTimeout = TimeSpan.FromSeconds(60);

    private readonly IToolRegistry _tools;
    private readonly IAgentApprovalGate _approvals;
    private readonly IPermissionService _permissions;
    private readonly ILogger<AgentExecutor> _logger;

    public AgentExecutor(
        IToolRegistry tools,
        IAgentApprovalGate approvals,
        IPermissionService permissions,
        ILogger<AgentExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(approvals);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(logger);

        _tools = tools;
        _approvals = approvals;
        _permissions = permissions;
        _logger = logger;
    }

    /// <summary>Raised as each step starts and finishes, for a timeline.</summary>
    public event EventHandler<AgentProgress>? ProgressChanged;

    /// <inheritdoc />
    public Task<Result<AgentPlan>> ValidateAsync(
        AgentPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var step in plan.Steps)
        {
            var check = CheckStep(plan, step);

            if (check.IsFailure)
            {
                return Task.FromResult(Result<AgentPlan>.Failure(check.ErrorCode!, check.ErrorMessage!));
            }
        }

        // Only here does a plan become runnable. A planner's output is a draft until this method
        // has agreed it, which is the property that makes it safe to show a plan before running.
        return Task.FromResult(Result<AgentPlan>.Success(plan.AsReady()));
    }

    /// <inheritdoc />
    public async Task<Result<AgentExecutionResult>> ExecuteAsync(
        AgentPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // Refused rather than validated and run in one step, so there is no path in which a plan
        // that was never checked reaches a tool.
        if (plan.Status != AgentPlanStatus.Ready)
        {
            return Result<AgentExecutionResult>.Failure(
                ErrorCodes.AgentStepFailed,
                "This plan has not been checked, so it was not run. Validate it first.");
        }

        var validated = await ValidateAsync(plan, cancellationToken);

        if (validated.IsFailure)
        {
            return Result<AgentExecutionResult>.Failure(validated.ErrorCode!, validated.ErrorMessage!);
        }

        var ready = validated.Value
            ?? throw new InvalidOperationException("A successful validation must carry the plan it checked.");

        return await RunAsync(ready, cancellationToken);
    }

    /// <inheritdoc />
    public Task<AgentApprovalDecision> RequestApprovalAsync(
        AgentApprovalRequest request,
        CancellationToken cancellationToken = default) =>
        _approvals.RequestApprovalAsync(request, cancellationToken);

    /// <inheritdoc />
    public bool TryResolveApproval(AgentApprovalDecision decision) => _approvals.TryResolve(decision);

    /// <summary>Walks the steps in order, stopping at the first one that cannot go on.</summary>
    private async Task<Result<AgentExecutionResult>> RunAsync(
        AgentPlan plan,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var context = new AgentExecutionContext(
            plan.Id,
            plan.OriginalRequest,
            plan.Source,
            conversationId: null);

        var steps = new List<AgentStep>(plan.Steps.Count);

        // Announced before the first step rather than folded into it, so an interface can draw
        // the whole plan and watch it run. Without this the first thing a person sees is step
        // one, and the plan they approved a moment earlier is a thing they have to remember.
        Raise(AgentProgress.PlanReady(plan.Id, plan, plan.Steps.Count));

        foreach (var step in plan.Steps)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(plan, steps, stopwatch.Elapsed, context);
            }

            Raise(AgentProgress.Started(plan.Id, step, steps.Count, plan.Steps.Count));

            if (!_tools.TryGet(step.ToolName, out var tool) || tool is null)
            {
                // Cannot happen after validation, but a plan that is checked and then has a tool
                // removed underneath it must fail rather than run the nearest other tool.
                return Fail(
                    plan,
                    steps,
                    step.AsFailed(
                        ErrorCodes.AgentToolUnknown,
                        $"There is no tool called {step.ToolName}."),
                    stopwatch.Elapsed,
                    context,
                    cancellationToken);
            }

            var outcome = await RunStepAsync(plan, step, tool, context, cancellationToken);
            steps.Add(outcome);

            Raise(AgentProgress.Finished(plan.Id, outcome, steps.Count, plan.Steps.Count));

            if (outcome.Status == AgentStepStatus.Rejected)
            {
                return Rejected(plan, steps, stopwatch.Elapsed, context);
            }

            if (outcome.Status == AgentStepStatus.Failed)
            {
                return Fail(plan, steps, outcome, stopwatch.Elapsed, context, cancellationToken);
            }

            context.RecordResult(KeyFor(outcome), outcome.Order, outcome.Result!);
        }

        return Result<AgentExecutionResult>.Success(AgentExecutionResult.Completed(
            plan.Id,
            plan,
            steps,
            Summarize(plan, steps, context),
            stopwatch.Elapsed,
            context.CollectSources(),
            BuildData(steps, context)));
    }

    /// <summary>
    /// Runs one step, holding it at the approval gate first when it would change something.
    /// <para>
    /// The gate is entered from here and nowhere else, and it is entered on the risk of the
    /// action rather than on the kind of tool. A read-only tool is never held; a tool that writes
    /// is always held, including one the planner believed was safe.
    /// </para>
    /// </summary>
    private async Task<AgentStep> RunStepAsync(
        AgentPlan plan,
        AgentStep step,
        ITool tool,
        AgentExecutionContext context,
        CancellationToken cancellationToken)
    {
        var pending = step with { Status = AgentStepStatus.Running };
        var started = Stopwatch.StartNew();

        var refusal = CheckConsent(tool, step);

        if (refusal is not null)
        {
            return step.AsFailed(refusal.Value.Code, refusal.Value.Message);
        }

        var request = BuildApprovalRequest(plan, step, tool, pending, started.Elapsed);

        if (request is not null)
        {
            Raise(AgentProgress.AwaitingApproval(plan.Id, pending, request, 0, plan.Steps.Count));

            var decision = await _approvals.RequestApprovalAsync(request, cancellationToken);

            if (!decision.AllowsExecution)
            {
                _logger.LogInformation(
                    "Step {Step} of run {RunId} stopped: approval outcome {Outcome}.",
                    step.Order,
                    plan.Id,
                    decision.Outcome);

                return step
                    .AwaitingApproval(request)
                    .AsRejected(decision);
            }

            if (decision.Outcome == AgentApprovalOutcome.Modified && decision.Modification is not null)
            {
                // The step is rewritten before it runs, so the tool receives the change as part
                // of its parameters rather than as a note it would have to guess the meaning of.
                pending = ApplyModification(pending, tool, decision.Modification);

                // A modification the tool could not interpret must end the run, not be discarded.
                // Continuing here would write a file with the name the person said no to, or
                // leave the subject the person changed in place — while the timeline showed the
                // run as proceeding. A person who corrected the agent and was then ignored has
                // been given worse information than one who was refused.
                if (pending.Status == AgentStepStatus.Failed)
                {
                    return pending;
                }
            }
        }

        var toolRequest = new ToolRequest(
            tool.Name,
            step.Description,
            plan.OriginalRequest,
            pending.Parameters,
            plan.Source)
        {
            // The one channel between steps. Gathered here and nowhere else, so a tool cannot read
            // an earlier result by any other route, and a planner cannot put one there.
            PriorContext = context.BuildContextText() is { Length: > 0 } prior ? prior : null,
        };

        ToolResult result;

        try
        {
            result = await tool.ExecuteAsync(toolRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return step.AsFailed(ErrorCodes.AgentCancelled, "The request was stopped.");
        }
        catch (Exception exception)
        {
            // A tool is contracted not to throw for anything a person can act on, so reaching
            // here is a bug in the tool rather than in the request. It is reported as a failed
            // step so the plan stops in a way the person can see, and logged in full because the
            // exception is where the detail lives.
            _logger.LogError(
                exception,
                "Tool {Tool} threw while running step {Step} of run {RunId}.",
                tool.Name,
                step.Order,
                plan.Id);

            return step.AsFailed(
                ErrorCodes.AgentStepFailed,
                $"{tool.Name} could not complete. Nothing was changed.");
        }

        return result.IsSuccess
            ? pending.AsSucceeded(result.WithDuration(started.Elapsed))
            : pending.AsFailed(
                result.ErrorCode ?? ErrorCodes.AgentStepFailed,
                result.ErrorMessage ?? $"{tool.Name} could not complete.");
    }

    /// <summary>
    /// Checks every reason one step cannot run, and reports the first. A person is told about one
    /// problem at a time: listing all four at once reads as though nothing could be done.
    /// </summary>
    private Result CheckStep(AgentPlan plan, AgentStep step)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(step);

        switch (_tools.Check(step.ToolName))
        {
            case ToolAvailability.Unregistered:
                return Result.Failure(
                    ErrorCodes.AgentToolUnknown,
                    $"Step {step.Order.ToString(CultureInfo.InvariantCulture)} asks for " +
                    $"{step.ToolName}, which this assistant does not have.");

            case ToolAvailability.Unavailable:
            case ToolAvailability.Misconfigured:
                return Result.Failure(
                    ErrorCodes.AgentToolUnavailable,
                    $"Step {step.Order.ToString(CultureInfo.InvariantCulture)} cannot run: " +
                    $"{_tools.GetUnavailableReason(step.ToolName) ?? "the capability is switched off"}.");
        }

        if (!_tools.TryGet(step.ToolName, out var tool) || tool is null)
        {
            return Result.Failure(
                ErrorCodes.AgentToolUnknown,
                $"Step {step.Order.ToString(CultureInfo.InvariantCulture)} asks for " +
                $"{step.ToolName}, which this assistant does not have.");
        }

        var missing = _tools.GetMissingInputs(step.ToolName, step.Parameters);

        if (missing.Count > 0)
        {
            return Result.Failure(
                ErrorCodes.AgentStepFailed,
                $"Step {step.Order.ToString(CultureInfo.InvariantCulture)} is missing " +
                $"{string.Join(" and ", missing)}.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Checks the consent switch a step depends on, returning a refusal when it is off.
    /// <para>
    /// The check is repeated at run time rather than trusted from validation, because a person
    /// can turn a setting off between a plan being shown and the plan being run — and the run is
    /// the moment it matters.
    /// </para>
    /// </summary>
    private (string Code, string Message)? CheckConsent(ITool tool, AgentStep step)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(step);

        var permission = tool.RequiredPermission;

        if (permission is null)
        {
            return null;
        }

        if (_permissions.IsGranted(permission.Value))
        {
            return null;
        }

        _logger.LogInformation(
            "Step {Step} needs {Permission}, which is not granted.",
            step.Order,
            permission);

        // The service's own wording is used when it has one, so the sentence a person reads here
        // is the same one they would get from the page that owns the switch.
        return (
            ErrorCodes.AgentToolUnavailable,
            _permissions.GetDeniedMessage(permission.Value));
    }

    /// <summary>
    /// Builds the approval a step needs, or <see langword="null"/> when it needs none.
    /// <para>
    /// A read-only tool never gets one. That is the single most important rule here: a plan made
    /// entirely of searches should never interrupt somebody with a dialog, and the surest way to
    /// guarantee that is for the gate to depend only on whether the tool declares an action.
    /// </para>
    /// </summary>
    private AgentApprovalRequest? BuildApprovalRequest(
        AgentPlan plan,
        AgentStep step,
        ITool tool,
        AgentStep pending,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(pending);

        var actions = tool.DescribeActions(pending);

        if (actions.Count == 0)
        {
            return null;
        }

        // The riskiest action decides the prompt. A step that writes to a temp file and also
        // writes to a document must be judged on the document.
        var action = actions.MaxBy(candidate => candidate.RiskLevel)
            ?? throw new InvalidOperationException(
                $"{tool.Name} declared an action list that is empty.");

        return AgentApprovalRequest.For(plan.Id, pending, action, DefaultApprovalTimeout);
    }

    /// <summary>
    /// Folds a person's modification into the step's parameters, when the tool says how.
    /// <para>
    /// A tool that cannot interpret a modification is not asked to. The change is reported as
    /// refused instead, because a step running with the modification silently dropped is exactly
    /// the case where a person believes they got what they asked for and did not.
    /// </para>
    /// </summary>
    private static AgentStep ApplyModification(AgentStep step, ITool tool, string modification)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(tool);

        if (!tool.TryApplyModification(step, modification, out var modified))
        {
            return step.AsFailed(
                ErrorCodes.AgentApprovalRejected,
                $"I could not apply that change: {modification}");
        }

        return modified;
    }

    /// <summary>Builds the result for a run that stopped because a person pressed stop.</summary>
    private static Result<AgentExecutionResult> Cancelled(
        AgentPlan plan,
        List<AgentStep> steps,
        TimeSpan elapsed,
        AgentExecutionContext context) =>
        Result<AgentExecutionResult>.Success(AgentExecutionResult.Stopped(
            plan.Id,
            plan,
            steps,
            "Stopped. Nothing else was done.",
            AgentPlanStatus.Cancelled,
            elapsed,
            context.CollectSources(),
            BuildData(steps, context),
            ErrorCodes.AgentCancelled));

    /// <summary>Builds the result for a run that stopped because a person refused a step.</summary>
    private static Result<AgentExecutionResult> Rejected(
        AgentPlan plan,
        List<AgentStep> steps,
        TimeSpan elapsed,
        AgentExecutionContext context)
    {
        var refused = steps.LastOrDefault(step => step.Status == AgentStepStatus.Rejected);
        var reason = refused?.ApprovalDecision?.ResponseText ?? "I have not done that.";

        return Result<AgentExecutionResult>.Success(AgentExecutionResult.Stopped(
            plan.Id,
            plan,
            steps,
            reason,
            AgentPlanStatus.Rejected,
            elapsed,
            context.CollectSources(),
            BuildData(steps, context),
            ErrorCodes.AgentApprovalRejected));
    }

    /// <summary>Builds the result for a run that stopped because a step failed.</summary>
    private static Result<AgentExecutionResult> Fail(
        AgentPlan plan,
        List<AgentStep> steps,
        AgentStep failed,
        TimeSpan elapsed,
        AgentExecutionContext context,
        CancellationToken cancellationToken)
    {
        // A failure caused by the person pressing stop is reported as a stop, not as an error.
        if (failed.ErrorCode == ErrorCodes.AgentCancelled || cancellationToken.IsCancellationRequested)
        {
            return Cancelled(plan, steps, elapsed, context);
        }

        var completed = steps.Count(step => step.Succeeded);

        var message =
            $"I stopped after {completed.ToString(CultureInfo.InvariantCulture)} " +
            $"of {plan.Steps.Count.ToString(CultureInfo.InvariantCulture)} step(s). " +
            (failed.ErrorMessage ?? $"{failed.ToolName} could not complete.");

        return Result<AgentExecutionResult>.Success(AgentExecutionResult.Stopped(
            plan.Id,
            plan,
            steps,
            message,
            AgentPlanStatus.Failed,
            elapsed,
            context.CollectSources(),
            BuildData(steps, context),
            failed.ErrorCode));
    }

    /// <summary>
    /// Builds the sentence shown at the end of a run, from what the steps reported rather than
    /// from a model. Every step of a demonstration has already said what it did, and a summary
    /// that rephrases those through a provider would be a second place for a person's document
    /// text to escape.
    /// </summary>
    private static string Summarize(AgentPlan plan, List<AgentStep> steps, AgentExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(context);

        var lines = steps
            .Where(step => step.Succeeded && !string.IsNullOrWhiteSpace(step.ResultSummary))
            .Select(step => step.ResultSummary!)
            .ToArray();

        if (lines.Length == 0)
        {
            return "Done.";
        }

        // The last step's own account of its result is the one a person wants, when the earlier
        // steps were preparation for it.
        return lines.Length == 1 ? lines[0] : string.Join("\n", lines);
    }

    /// <summary>
    /// Collects the metadata a run is allowed to keep. Built from step summaries, counts, and
    /// names — the whole of <see cref="ToolResult.Data"/> — and from no step's content, because
    /// this object is what gets written to the activity store.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildData(
        List<AgentStep> steps,
        AgentExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(context);

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tools"] = string.Join(", ", steps.Where(step => step.Succeeded).Select(step => step.ToolName)),
            ["stepCount"] = steps.Count.ToString(CultureInfo.InvariantCulture),
            ["sourceCount"] = context.CollectSources().Count.ToString(CultureInfo.InvariantCulture),
        };

        foreach (var step in steps.Where(step => step.Succeeded))
        {
            foreach (var pair in step.Result!.Data)
            {
                data[$"{step.ToolName}.{pair.Key}"] = pair.Value;
            }
        }

        return data;
    }

    /// <summary>Builds the key a step's result is stored under for later steps to read.</summary>
    private static string KeyFor(AgentStep step) => $"{step.Order.ToString(CultureInfo.InvariantCulture)}:{step.ToolName}";

    private void Raise(AgentProgress progress) => ProgressChanged?.Invoke(this, progress);
}
