using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Automations;

namespace WindowsAIAssistant.App.Services;

public sealed class AutomationScheduler(IAutomationService automations, ILogger<AutomationScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the shell time to initialize before delivering overdue reminders.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        var pending = new List<Task>();
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                pending.RemoveAll(task => task.IsCompleted);
                // Keep polling while documents are being analyzed, so newly due reminders are timely.
                // The service claims each occurrence under its gate and skips already running jobs.
                pending.Add(ProcessDueAsync(stoppingToken));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await Task.WhenAll(pending); }
    }

    private async Task ProcessDueAsync(CancellationToken token)
    {
        try { await automations.RunDueAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "Automation scheduler could not process schedules."); }
    }
}
