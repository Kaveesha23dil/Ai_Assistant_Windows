using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Infrastructure.Computer;

/// <summary>
/// Runs several computer actions as one sequence, stopping at the first failure unless a step
/// says otherwise.
/// <para>
/// A workflow is a convenience over the single-action service, never a second way to reach the
/// desktop. Every step goes through <see cref="IComputerControlService"/>, so the same
/// validation, permission, rate limit, timeout, and cancellation rules apply exactly as they do
/// to one action. What this adds is sequencing: the whole run has one deadline, one cancellation
/// signal, and one answer that says which step failed.
/// </para>
/// </summary>
public sealed class ComputerWorkflowExecutor : IComputerWorkflowExecutor
{
    private readonly IComputerControlService _control;
    private readonly IComputerContextService _context;
    private readonly ILogger<ComputerWorkflowExecutor> _logger;

    public ComputerWorkflowExecutor(
        IComputerControlService control,
        IComputerContextService context,
        ILogger<ComputerWorkflowExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        _control = control;
        _context = context;
        _logger = logger;
    }

    public async Task<Result<ComputerWorkflowResult>> ExecuteAsync(
        ComputerWorkflow workflow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        if (workflow.Steps.Count == 0)
        {
            return Result<ComputerWorkflowResult>.Failure(
                ErrorCodes.ComputerTargetNotFound,
                "That workflow has no steps to run.");
        }

        var timeout = workflow.Timeout <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(2)
            : workflow.Timeout;

        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadline.Token);

        var stopwatch = Stopwatch.StartNew();
        var results = new List<ComputerActionResult>(workflow.Steps.Count);
        var completed = 0;
        int? failedStep = null;
        string message = "The workflow finished.";

        for (var index = 0; index < workflow.Steps.Count; index++)
        {
            if (linked.IsCancellationRequested)
            {
                message = "The workflow was stopped.";
                break;
            }

            var step = workflow.Steps[index];
            var request = new ComputerActionRequest
            {
                Action = step.Action,
                Purpose = step.Description,
                IsConfirmed = workflow.IsConfirmed,
                IsSafeMode = workflow.IsSafeMode
            };

            var result = await _control.ExecuteAsync(request, linked.Token).ConfigureAwait(false);

            if (result.IsFailure || result.Value is null)
            {
                var failure = ComputerActionResult.Failure(
                    step.Action.Type,
                    result.ErrorCode ?? ErrorCodes.ComputerActionFailed,
                    result.ErrorMessage ?? "The workflow step failed.");

                results.Add(failure);
                failedStep = index + 1;

                if (linked.IsCancellationRequested)
                {
                    message = "The workflow was stopped.";
                    break;
                }

                if (!step.ContinueOnFailure)
                {
                    message = $"Step {failedStep} failed: {failure.Message}";
                    break;
                }

                continue;
            }

            var actionResult = result.Value;
            var verification = await VerifyAsync(step, actionResult, linked.Token)
                .ConfigureAwait(false);

            if (verification is not null)
            {
                var failed = ComputerActionResult.Failure(
                    step.Action.Type,
                    ErrorCodes.ComputerVerificationFailed,
                    verification);

                results.Add(failed);
                failedStep = index + 1;

                if (!step.ContinueOnFailure)
                {
                    message = $"Step {failedStep} was not confirmed: {verification}";
                    break;
                }

                continue;
            }

            results.Add(actionResult);
            completed++;

            if (step.WaitAfter > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(step.WaitAfter, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    message = "The workflow was stopped.";
                    break;
                }
            }
        }

        stopwatch.Stop();
        var cancelled = linked.IsCancellationRequested && failedStep is null;

        if (cancelled)
        {
            message = "The workflow was stopped.";
        }

        return Result<ComputerWorkflowResult>.Success(new ComputerWorkflowResult
        {
            WorkflowName = workflow.Name,
            Succeeded = failedStep is null && completed == workflow.Steps.Count && !cancelled,
            Message = message,
            CompletedSteps = completed,
            TotalSteps = workflow.Steps.Count,
            FailedStep = failedStep,
            StepResults = results,
            Duration = stopwatch.Elapsed,
            WasCancelled = cancelled
        });
    }

    private async Task<string?> VerifyAsync(
        ComputerWorkflowStep step,
        ComputerActionResult result,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(step.ExpectedElementName))
        {
            var name = result.Element?.Label;

            if (name is null
                || !name.Contains(step.ExpectedElementName, StringComparison.OrdinalIgnoreCase))
            {
                return $"I expected the element '{step.ExpectedElementName}'.";
            }
        }

        if (!string.IsNullOrWhiteSpace(step.ExpectedWindowTitle))
        {
            var title = result.ActiveWindow?.Title;

            if (title is null)
            {
                var context = await _context.GetContextAsync(cancellationToken).ConfigureAwait(false);
                if (context.IsSuccess)
                {
                    title = context.Value?.ActiveWindow?.Title;
                }
            }

            if (title is null
                || !title.Contains(step.ExpectedWindowTitle, StringComparison.OrdinalIgnoreCase))
            {
                return $"I expected the window '{step.ExpectedWindowTitle}' to be active.";
            }
        }

        return null;
    }
}
