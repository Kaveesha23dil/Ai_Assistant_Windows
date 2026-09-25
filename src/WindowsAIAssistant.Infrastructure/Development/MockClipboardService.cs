using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Infrastructure.Development;

public sealed class MockClipboardService : IClipboardService
{
    private string? _text;

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Volatile.Read(ref _text));
    }

    public Task<Result> SetTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (text is null)
        {
            return Task.FromResult(Result.Failure("Clipboard text cannot be null."));
        }

        Volatile.Write(ref _text, text);
        return Task.FromResult(Result.Success());
    }
}
