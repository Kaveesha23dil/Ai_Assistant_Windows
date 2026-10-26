using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Core.Abstractions.Computer;

public interface IWindowManager
{
    Task<Result<IReadOnlyList<WindowInfo>>> GetWindowsAsync(
        bool includeHidden,
        CancellationToken cancellationToken = default);

    Task<Result<WindowInfo>> GetActiveWindowAsync(CancellationToken cancellationToken = default);

    Task<Result<WindowInfo>> FindWindowAsync(
        string titleOrProcess,
        CancellationToken cancellationToken = default);

    Task<Result> SwitchToAsync(
        WindowInfo window,
        CancellationToken cancellationToken = default);

    Task<Result> MinimizeAsync(WindowInfo window, CancellationToken cancellationToken = default);

    Task<Result> MaximizeAsync(WindowInfo window, CancellationToken cancellationToken = default);

    Task<Result> RestoreAsync(WindowInfo window, CancellationToken cancellationToken = default);

    Task<Result> CloseAsync(WindowInfo window, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<MonitorInfo>>> GetMonitorsAsync(
        CancellationToken cancellationToken = default);

    Task<Result<ComputerRect>> GetCursorPositionAsync(CancellationToken cancellationToken = default);
}
