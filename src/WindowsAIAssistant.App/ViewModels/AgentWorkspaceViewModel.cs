using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WindowsAIAssistant.Application.Agents;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>One line in the run timeline.</summary>
public sealed record AgentStepViewModel(
    int Order,
    string ToolName,
    string Description,
    string Status,
    string Detail);

/// <summary>One capability, and the reason when it is switched off.</summary>
public sealed record AgentCapabilityViewModel(
    string Title,
    string Description,
    bool IsAvailable,
    string Mark,
    string? UnavailableReason);

/// <summary>One demonstration card.</summary>
/// <param name="Run">Runs this demonstration. Carried on the card rather than bound through the
/// page, so the button does not have to reach out of its own data template to find a command.</param>
public sealed record AgentDemoViewModel(
    string Id,
    string Title,
    string Prompt,
    string Why,
    string ExpectedTools,
    bool IsAvailable,
    string? UnavailableReason,
    IRelayCommand Run)
{
    /// <summary>
    /// Gets whether to show the reason. A card whose reason is hidden is not dimmed, so the
    /// dimming is done by the whole card being unavailable rather than by a missing line.
    /// </summary>
    public Visibility UnavailableReasonVisibility => IsAvailable
        ? Visibility.Collapsed
        : Visibility.Visible;

    /// <summary>Gets the card's own opacity, so a dimmed card is visibly not ready.</summary>
    public double CardOpacity => IsAvailable ? 1.0 : 0.6;
}

/// <summary>
/// A step the run is stopped on, waiting to be answered.
/// <para>
/// Carries the identifier of the approval as well as its wording, because a prompt that shows
/// but cannot be answered is a dialog somebody learns to dismiss with Escape.
/// </para>
/// </summary>
public sealed record AgentApprovalViewModel(
    Guid ApprovalId,
    string Action,
    string Description,
    PermissionCapability? RequiredPermission);

/// <summary>
/// The agent workspace: a request, the plan it produced, the tools as they run, the approval
/// when one is needed, and the capability list with the reason anything is switched off.
/// <para>
/// The page is built around one decision: it shows the machine working, not the machine
/// answering. A run has a plan before it has a result, tools have names before they have
/// outcomes, and a step that would change something stops and asks in the middle of the timeline
/// rather than after the fact. A person watching should be able to say what was about to happen
/// before it happened, and that is the only claim this view model makes.
/// </para>
/// <para>
/// It holds no document text, no screen content, and no request. The only things that cross from
/// a run to here are names, counts, statuses, and the sentences the agent itself wrote — the
/// same rule the activity store keeps to, applied one layer earlier so the interface is not a
/// place a step's material leaks through.
/// </para>
/// </summary>
public sealed partial class AgentWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly IAgent _agent;
    private readonly IAgentCapabilityReporter _capabilities;
    private readonly IToolRegistry _tools;
    private readonly IAgentActivityStore _activity;
    private readonly IAgentMemoryService _memory;
    private readonly DispatcherQueue _dispatcher;

    private CancellationTokenSource? _cancellation;
    private bool _disposed;

    public AgentWorkspaceViewModel(
        IAgent agent,
        IAgentCapabilityReporter capabilities,
        IToolRegistry tools,
        IAgentActivityStore activity,
        IAgentMemoryService memory)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(memory);

        _agent = agent;
        _capabilities = capabilities;
        _tools = tools;
        _activity = activity;
        _memory = memory;

        // Progress arrives on whichever thread the tool finished on, so every change it causes
        // is marshalled here. Captured once at construction, which is the only moment a page
        // view model is guaranteed to be on the thread that owns the view.
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _agent.ProgressChanged += OnAgentProgress;
    }

    /// <summary>Gets what this machine can do, in the order it should be read.</summary>
    public ObservableCollection<AgentCapabilityViewModel> Capabilities { get; } = [];

    /// <summary>Gets the demonstrations, cheapest and most certain first.</summary>
    public ObservableCollection<AgentDemoViewModel> Demonstrations { get; } = [];

    /// <summary>Gets the steps of the run in progress, oldest first.</summary>
    public ObservableCollection<AgentStepViewModel> Steps { get; } = [];

    /// <summary>Gets the recent runs, newest first.</summary>
    public ObservableCollection<AgentActivity> Timeline { get; } = [];

    /// <summary>Gets the approval the run is waiting on, or <see langword="null"/> when it is not waiting.</summary>
    [ObservableProperty]
    public partial AgentApprovalViewModel? PendingApproval { get; set; }

    [ObservableProperty]
    public partial string Draft { get; set; } = string.Empty;

    /// <summary>
    /// The change typed into the approval prompt, if any. Bound two-way because it is the one
    /// place a person alters a run rather than starting one.
    /// </summary>
    [ObservableProperty]
    public partial string Modification { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Response { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusLine { get; set; } = "Ready.";

    [ObservableProperty]
    public partial string PlanSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    /// <summary>Gets the one-line summary, such as "6 of 8 available".</summary>
    public string AvailabilitySummary { get; private set; } = string.Empty;

    public bool HasDraft => !string.IsNullOrWhiteSpace(Draft);

    public bool HasResponse => !string.IsNullOrWhiteSpace(Response);

    public bool IsIdle => !IsRunning && PendingApproval is null;

    /// <summary>
    /// Gets whether the approval prompt is showing. The prompt lives in the middle of the run
    /// rather than over the top of it, so the run stays readable behind it and the thing being
    /// asked about is still on screen while it is being answered.
    /// </summary>
    public Visibility ApprovalVisibility => PendingApproval is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    /// <summary>Gets whether the answer block is worth showing at all.</summary>
    public Visibility ResponseVisibility => string.IsNullOrWhiteSpace(Response)
        ? Visibility.Collapsed
        : Visibility.Visible;

    /// <summary>
    /// A run can be started when there is something to run and nothing already running. An
    /// approval being outstanding is not a reason to block it — a run is held at a prompt, and
    /// refusing to start the next one would mean the machine is idle and still says it is busy.
    /// </summary>
    public bool CanRun => !IsRunning && !string.IsNullOrWhiteSpace(Draft);

    partial void OnDraftChanged(string value)
    {
        OnPropertyChanged(nameof(HasDraft));
        OnPropertyChanged(nameof(CanRun));
        RunCommand.NotifyCanExecuteChanged();
    }

    partial void OnResponseChanged(string value)
    {
        OnPropertyChanged(nameof(HasResponse));
        OnPropertyChanged(nameof(ResponseVisibility));
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        RunCommand.NotifyCanExecuteChanged();
    }

    partial void OnPendingApprovalChanged(AgentApprovalViewModel? value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(ApprovalVisibility));

        // Cleared with the prompt, so a change typed for one step is not sitting in the box
        // being offered to the next one.
        if (value is null)
        {
            Modification = string.Empty;
        }
    }

    /// <summary>
    /// Reads the capability list, the demonstrations, and the recent runs.
    /// <para>
    /// Called every time the page appears rather than once at construction. Availability is a
    /// property of the switches, and a person who has just turned one on — on this page or in
    /// settings — should see the card light up without having to restart the application.
    /// </para>
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        LoadCapabilities();
        LoadDemonstrations();
        await LoadTimelineAsync();
    }

    /// <summary>Sends the typed request.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var request = Draft.Trim();

        if (request.Length == 0)
        {
            return;
        }

        await ExecuteAsync(AgentRequestContext.Typed(request));

        Draft = string.Empty;
    }

    /// <summary>
    /// Runs a demonstration, marked as such so the timeline can say it was a showcase rather
    /// than something the person needed.
    /// </summary>
    [RelayCommand]
    private async Task RunDemoAsync(AgentDemoViewModel? demo)
    {
        ArgumentNullException.ThrowIfNull(demo);

        if (!demo.IsAvailable)
        {
            // Refused here, with the reason, rather than by the executor. The card already says
            // why it is dimmed; pressing it should say the same thing in the same words.
            StatusLine = demo.UnavailableReason ?? "That demonstration is switched off.";
            return;
        }

        await ExecuteAsync(AgentRequestContext.Demo(
            new DemoScenario(
                demo.Id,
                demo.Title,
                demo.Prompt,
                demo.Why,
                demo.ExpectedTools.Split(", ", StringSplitOptions.RemoveEmptyEntries),
                [])));
    }
    /// <summary>Stops the run in progress.</summary>
    [RelayCommand]
    private void Stop()
    {
        _cancellation?.Cancel();
        StatusLine = "Stopping.";
    }

    /// <summary>Approves the step the run is waiting on.</summary>
    [RelayCommand]
    private Task ApproveAsync() => RespondAsync(approved: true, modification: null);

    /// <summary>Refuses the step the run is waiting on.</summary>
    [RelayCommand]
    private Task RejectAsync() => RespondAsync(approved: false, modification: null);

    /// <summary>
    /// Approves the step with a change, which the tool folds in if it knows how and the run
    /// stops if it does not.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmAsync(string? modification)
    {
        var change = modification?.Trim();

        await RespondAsync(approved: true, modification: change is { Length: > 0 } ? change : null);
    }

    /// <summary>Forgets everything remembered, after the person asks.</summary>
    [RelayCommand]
    private async Task ForgetAsync()
    {
        var cleared = await _memory.ForgetAllAsync(cancellationToken: CancellationToken.None);

        StatusLine = cleared > 0
            ? $"Forgot {cleared} thing(s)."
            : "There was nothing to forget.";
    }

    private async Task RespondAsync(bool approved, string? modification)
    {
        var pending = PendingApproval;

        if (pending is null)
        {
            return;
        }

        var decision = (approved, modification) switch
        {
            (true, null) => AgentApprovalDecision.Approve(pending.ApprovalId),
            (true, var change) => AgentApprovalDecision.Modify(pending.ApprovalId, change!),
            _ => AgentApprovalDecision.Reject(pending.ApprovalId),
        };

        // The answer is cleared before it is sent, and restored if the run was no longer
        // waiting. A prompt that has timed out stays on screen until a person deals with it, and
        // one that silently does nothing when pressed is worse than one that explains itself.
        PendingApproval = null;

        if (!await _agent.RespondToApprovalAsync(decision))
        {
            StatusLine = "That step was no longer waiting for an answer.";
        }
    }

    /// <summary>Runs a request and turns the result into something to read.</summary>
    private async Task ExecuteAsync(AgentRequestContext context)
    {
        if (IsRunning)
        {
            StatusLine = "Something is already running.";
            return;
        }

        Steps.Clear();
        PlanSummary = string.Empty;
        Response = string.Empty;
        IsRunning = true;
        StatusLine = "Planning…";

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        try
        {
            var result = await _agent.RunAsync(context, cancellation.Token);

            if (result.IsFailure || result.Value is null)
            {
                Response = string.Empty;
                StatusLine = result.ErrorMessage ?? "I could not do that.";
            }
            else
            {
                var finished = result.Value;
                Response = finished.FinalResponse;
                PlanSummary = Describe(finished.Plan);
                StatusLine = finished.Status switch
                {
                    AgentPlanStatus.Completed => "Done.",
                    AgentPlanStatus.Rejected => "Stopped — you said no.",
                    AgentPlanStatus.Cancelled => "Stopped.",
                    _ => "Stopped part-way through.",
                };
            }
        }
        catch (OperationCanceledException)
        {
            StatusLine = "Stopped.";
        }
        finally
        {
            _cancellation = null;
            IsRunning = false;
            PendingApproval = null;

            await LoadTimelineAsync();
        }
    }

    /// <summary>Builds the one-line plan summary shown above the steps.</summary>
    private static string Describe(AgentPlan plan) =>
        plan.Steps.Count == 0
            ? "Nothing to do."
            : $"{plan.Steps.Count} step(s): {string.Join(" → ", plan.Steps.Select(step => step.ToolName))}";

    private void LoadCapabilities()
    {
        Capabilities.Clear();

        foreach (var capability in _capabilities.Report())
        {
            Capabilities.Add(new AgentCapabilityViewModel(
                capability.Title,
                capability.Description,
                capability.IsAvailable,
                capability.Mark,
                capability.UnavailableReason));
        }

        var (available, total) = _capabilities.Summarize();

        AvailabilitySummary = $"{available} of {total} available";
    }

    private void LoadDemonstrations()
    {
        Demonstrations.Clear();

        foreach (var scenario in AgentDemoScenarios.All)
        {
            var missing = AgentDemoScenarios.UnavailableTools(
                scenario,
                tool => _tools.Check(tool) == ToolAvailability.Available);

            Demonstrations.Add(new AgentDemoViewModel(
                scenario.Id,
                scenario.Title,
                scenario.Prompt,
                scenario.Description,
                string.Join(", ", scenario.ExpectedTools),
                missing.Count == 0,
                missing.Count == 0
                    ? null
                    : $"Needs {string.Join(" and ", missing)}, which is switched off.",
                RunDemoCommand));
        }
    }

    private async Task LoadTimelineAsync()
    {
        var recent = await _activity.GetRecentAsync(15, CancellationToken.None);

        Timeline.Clear();

        foreach (var entry in recent)
        {
            Timeline.Add(entry);
        }
    }

    /// <summary>
    /// Turns progress into lines. Every line is a name, a status, or a sentence the agent wrote:
    /// nothing a step read reaches the page, which is the same rule the timeline keeps to.
    /// </summary>
    private void OnAgentProgress(object? sender, AgentProgress progress) =>
        OnUiThread(() =>
        {
            switch (progress.Kind)
            {
                case AgentProgressKind.PlanReady when progress.Plan is not null:
                    PlanSummary = Describe(progress.Plan);
                    StatusLine = "Running…";
                    break;

                case AgentProgressKind.StepStarted:
                case AgentProgressKind.StepFinished:
                    Upsert(progress.Step);
                    StatusLine = $"{progress.CompletedSteps} of {progress.TotalSteps} step(s)";
                    break;

                case AgentProgressKind.AwaitingApproval:
                    PendingApproval = progress.Approval is null
                        ? null
                        : new AgentApprovalViewModel(
                            progress.Approval.Id,
                            progress.Approval.Action,
                            progress.Approval.Description,
                            progress.Approval.RequiredPermission);

                    Upsert(progress.Step);
                    StatusLine = "Waiting for you.";
                    break;

                case AgentProgressKind.Completed:
                    if (progress.Plan is not null)
                    {
                        PlanSummary = Describe(progress.Plan);
                    }

                    StatusLine = progress.Message ?? "Done.";
                    break;
            }
        });

    /// <summary>
    /// Runs a change on the thread that owns the view, whether or not the caller is already on
    /// it. Bound collections in particular throw if they are changed from another thread, and
    /// the failure is intermittent enough to be very hard to attribute.
    /// </summary>
    private void OnUiThread(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            // Guarded, because a run that was stopped or a page that was closed can finish after
            // this view model is gone, and a collection that is no longer bound does not care
            // what a stray progress report has to say.
            if (!_disposed)
            {
                action();
            }
        });
    }

    /// <summary>
    /// Adds a step, or updates the one already shown for that position. A step is announced when
    /// it starts and again when it finishes, and the list should read as one entry per step
    /// rather than growing by two.
    /// </summary>
    private void Upsert(AgentStep? step)
    {
        if (step is null)
        {
            return;
        }

        var existing = Steps.FirstOrDefault(candidate => candidate.Order == step.Order);

        if (existing is null)
        {
            Steps.Add(new AgentStepViewModel(
                step.Order,
                step.ToolName,
                step.Description,
                Describe(step.Status),
                string.Empty));

            return;
        }

        var index = Steps.IndexOf(existing);

        Steps[index] = existing with
        {
            Status = Describe(step.Status),
            Detail = step.ErrorMessage ?? step.ResultSummary ?? string.Empty,
        };
    }

    private static string Describe(AgentStepStatus status) => status switch
    {
        AgentStepStatus.Pending => "Waiting",
        AgentStepStatus.Running => "Running",
        AgentStepStatus.AwaitingApproval => "Asking",
        AgentStepStatus.Succeeded => "Done",
        AgentStepStatus.Failed => "Failed",
        AgentStepStatus.Rejected => "Refused",
        AgentStepStatus.Skipped => "Skipped",
        _ => "Unknown",
    };

    /// <summary>
    /// Unsubscribes from the run's progress.
    /// <para>
    /// A singleton, so this is called when the window closes rather than when a page is
    /// navigated away from. That is the right time: the agent outlives the page, and a run
    /// in progress should keep reporting into a view model that is no longer on screen.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _agent.ProgressChanged -= OnAgentProgress;
        _cancellation?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
