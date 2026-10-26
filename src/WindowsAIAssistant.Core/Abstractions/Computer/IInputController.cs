using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Computer;

namespace WindowsAIAssistant.Core.Abstractions.Computer;

public interface IInputController
{
    Task<Result> ClickAsync(MouseInput input, CancellationToken cancellationToken = default);

    Task<Result> MoveAsync(int x, int y, CancellationToken cancellationToken = default);

    Task<Result> ScrollAsync(int delta, CancellationToken cancellationToken = default);

    Task<Result> TypeTextAsync(string text, CancellationToken cancellationToken = default);

    Task<Result> PressKeyAsync(KeyboardInput input, CancellationToken cancellationToken = default);

    Task<Result> PressShortcutAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken = default);
}
