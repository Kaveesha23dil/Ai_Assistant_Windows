using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents;

/// <summary>
/// The assistant's front door: takes a request, plans it, checks the plan, runs it, and records
/// what happened.
/// <para>
/// The sequence lives here rather than in each caller, because there are three callers — the
/// chat page, the workspace, and the voice path — and the fourth step of the sequence, waiting
/// for a person to answer a prompt, is the one most likely to be got wrong three different ways.
/// One implementation means a typed request and a spoken one are the same request.
/// </para>
/// <para>
/// A run holds a lock, not a queue. Two runs at once on one machine would interleave their tool
/// calls, their progress, and their approvals into something neither caller could describe, so
/// the second is refused with a sentence rather than made to wait invisibly.
/// </para>
/// <para>
/// What is recorded is metadata: which tools ran, how long, what came of it. The material a
/// step read is released when the run ends. That is not a retention policy applied afterwards —
/// the recording happens on objects that never held the material in the first place.
/// </para>
/// </summary>
public sealed class AgentService : IAgent
{
    private readonly IAgentPlanner _planner;
    private readonly IAgentExecutor _executor;
    private readonly IAgentApprovalGate _approvals;
    private readonly IAgentActivityStore _activity;
    private readonly ILogger<AgentService> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public AgentService(
        IAgentPlanner planner,
        IAgentExecutor executor,
        IAgentApprovalGate approvals,
        IAgentActivityStore activity,
        ILogger<AgentService> logger)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(approvals);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(logger);

        _planner = planner;
        _executor = executor;
        _approvals = approvals;
        _activity = activity;
        _logger = logger;

        _executor.ProgressChanged += OnExecutorProgress;
    }

    /// <inheritdoc />
    public event EventHandler<AgentProgress>? ProgressChanged;

    /// <inheritdoc />
    public AgentPlan? CurrentPlan { get; private set; }

    /// <inheritdoc />
    public bool IsRunning => _gate.CurrentCount == 0;

    /// <inheritdoc />
    public async Task<Result<AgentExecutionResult>> RunAsync(
        AgentRequestContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return Result<AgentExecutionResult>.Failure(
                ErrorCodes.AgentBusy,
                "I am already working on something. Wait for that to finish, or stop it.");
        }

        try
        {
            var plan = await _planner.CreatePlanAsync(context, cancellationToken);

            if (plan.IsFailure || plan.Value is null)
            {
                var message = plan.ErrorMessage ?? "I could not work out what to do with that.";

                // Recorded for the same reason a failed validation is: a machine that refuses
                // everything and a machine nobody is asking anything look identical from the
                // outside, and the timeline is the only thing that tells them apart.
                await _activity.RecordAsync(
                    AgentActivity.Failed(context, message, plan.ErrorCode),
                    CancellationToken.None);

                return Result<AgentExecutionResult>.Failure(
                    plan.ErrorCode ?? ErrorCodes.AgentIntentUnrecognized,
                    message);
            }

            CurrentPlan = plan.Value;

            // Checked and shown before it runs. A plan that cannot be checked never reaches a
            // tool, and a person watching the timeline sees what was going to happen rather than
            // being told afterwards.
            var validated = await _executor.ValidateAsync(plan.Value, cancellationToken);

            if (validated.IsFailure || validated.Value is null)
            {
                var message = validated.ErrorMessage ?? "That plan cannot be run on this machine.";

                await _activity.RecordAsync(
                    AgentActivity.Failed(context, message),
                    cancellationToken);

                return Result<AgentExecutionResult>.Failure(
                    validated.ErrorCode ?? ErrorCodes.AgentStepFailed,
                    message);
            }

            var ready = validated.Value;
            CurrentPlan = ready;

            var result = await _executor.ExecuteAsync(ready, cancellationToken);

            if (result.IsFailure || result.Value is null)
            {
                return Result<AgentExecutionResult>.Failure(
                    result.ErrorCode ?? ErrorCodes.AgentStepFailed,
                    result.ErrorMessage ?? "The request could not be completed.");
            }

            var finished = result.Value;

            await _activity.RecordAsync(
                AgentActivity.FromResult(context, finished),
                CancellationToken.None);

            Raise(AgentProgress.Completed(ready.Id, finished.Plan, finished.Status, finished.FinalResponse));

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("A run was stopped by the person who started it.");

            // Reported as a stop rather than a failure, because that is what happened and the
            // person reading it pressed the button. The interface says as much, and a caller
            // that has to guess would show "something went wrong" for the one outcome the
            // person caused deliberately. A run cancelled between steps has no plan to report
            // against, so the response is the sentence and nothing else.
            return Result<AgentExecutionResult>.Success(AgentExecutionResult.Stopped(
                CurrentPlan?.Id ?? Guid.NewGuid(),
                CurrentPlan ?? AgentPlan.Create(context.Request, [], context.Request, context.Source),
                [],
                "Stopped. Nothing else was done.",
                AgentPlanStatus.Cancelled,
                TimeSpan.Zero,
                [],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ErrorCodes.AgentCancelled));
        }
        finally
        {
            CurrentPlan = null;
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<bool> RespondToApprovalAsync(AgentApprovalDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return Task.FromResult(_approvals.TryResolve(decision));
    }

    /// <summary>Forwards the executor's progress, so a caller sees one stream rather than two.</summary>
    private void OnExecutorProgress(object? sender, AgentProgress progress) =>
        ProgressChanged?.Invoke(this, progress);

    private void Raise(AgentProgress progress) => ProgressChanged?.Invoke(this, progress);
}
