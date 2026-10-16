using WindowsAIAssistant.Core.Models.Automations;

namespace WindowsAIAssistant.Core.Abstractions.Automations;

public interface IAutomationService
{
    event EventHandler<Automation>? Completed;
    Task<IReadOnlyList<Automation>> ListAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(Automation automation, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default);
    Task RunAsync(Guid id, CancellationToken cancellationToken = default);
    Task RunDueAsync(CancellationToken cancellationToken = default);
}
