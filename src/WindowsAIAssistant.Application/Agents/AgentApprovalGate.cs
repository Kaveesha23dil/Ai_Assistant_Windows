using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents;

/// <summary>
/// Holds the approvals that have been asked for and are waiting for an answer.
/// <para>
/// An approval is a promise that a person will be told and will answer, so the gate has to
/// outlive the method that raised it: the executor awaits a completion source that this object
/// owns, and the interface calls <see cref="TryResolve"/> from somewhere else entirely — a
/// button, or a spoken answer — on a different turn of the loop.
/// </para>
/// <para>
/// Every request is stored against its own identity and removed the moment it is answered, so
/// a second answer to the same question is ignored rather than applied twice, and a run that
/// ends while a request is outstanding takes its request with it. Nothing accumulates for
/// waiting.
/// </para>
/// </summary>
public sealed class AgentApprovalGate : IAgentApprovalGate
{
    private readonly ConcurrentDictionary<Guid, PendingApproval> _pending = new();
    private readonly TimeProvider _clock;
    private readonly ILogger<AgentApprovalGate> _logger;

    public AgentApprovalGate(ILogger<AgentApprovalGate> logger, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Gets how many approvals are outstanding. For a status line and for tests.</summary>
    public int PendingCount => _pending.Count;

    /// <inheritdoc />
    public Task<AgentApprovalDecision> RequestApprovalAsync(
        AgentApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Already past the deadline by the time the prompt would be raised. Answered here rather
        // than shown, because showing a prompt that cannot be answered in time is worse than
        // reporting the refusal.
        if (request.TimesOutAt is { } expires && _clock.GetUtcNow() >= expires)
        {
            return Task.FromResult(AgentApprovalDecision.TimedOut(request.Id));
        }

        var pending = new PendingApproval(request);

        if (!_pending.TryAdd(request.Id, pending))
        {
            return Task.FromResult(AgentApprovalDecision.Reject(request.Id));
        }

        _logger.LogInformation(
            "Approval {ApprovalId} raised for step {Step} at {RiskLevel}; no action has been taken.",
            request.Id,
            request.StepOrder,
            request.RiskLevel);

        return AwaitDecisionAsync(pending, cancellationToken);
    }

    /// <inheritdoc />
    public bool TryResolve(AgentApprovalDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (!_pending.TryRemove(decision.Id, out var pending))
        {
            // Answered already, or the run ended and took the prompt with it. Either way there is
            // nothing to do, and saying so is better than completing the source a second time.
            _logger.LogInformation(
                "Approval {ApprovalId} was answered but is no longer outstanding.",
                decision.Id);

            return false;
        }

        pending.Completion.TrySetResult(decision);

        _logger.LogInformation(
            "Approval {ApprovalId} answered with {Outcome}.",
            decision.Id,
            decision.Outcome);

        return true;
    }

    /// <summary>
    /// Waits for an answer, and gives up on its own terms if the request expires or the run is
    /// cancelled. Every path out of this method completes the source exactly once, so the caller
    /// can await it without a lock of its own.
    /// </summary>
    private async Task<AgentApprovalDecision> AwaitDecisionAsync(
        PendingApproval pending,
        CancellationToken cancellationToken)
    {
        var request = pending.Request;

        using var expiry = new CancellationTokenSource();

        if (request.TimesOutAt is { } expires)
        {
            var remaining = expires - _clock.GetUtcNow();

            if (remaining <= TimeSpan.Zero)
            {
                expiry.Cancel();
            }
            else
            {
                expiry.CancelAfter(remaining);
            }
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(expiry.Token, cancellationToken);

        try
        {
            return await pending.Completion.Task.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (expiry.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Expired, or the run was cancelled. The source is cleared in both cases so a
            // late answer from a person who is still looking at the prompt cannot revive a
            // step that has already been given up on.
            _pending.TryRemove(request.Id, out _);

            return cancellationToken.IsCancellationRequested
                ? AgentApprovalDecision.Cancelled(request.Id)
                : AgentApprovalDecision.TimedOut(request.Id);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(request.Id, out _);
            return AgentApprovalDecision.Cancelled(request.Id);
        }
    }

    /// <summary>One outstanding approval, and the one thing a caller is waiting on.</summary>
    private sealed class PendingApproval(AgentApprovalRequest request)
    {
        public AgentApprovalRequest Request { get; } = request;

        public TaskCompletionSource<AgentApprovalDecision> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
