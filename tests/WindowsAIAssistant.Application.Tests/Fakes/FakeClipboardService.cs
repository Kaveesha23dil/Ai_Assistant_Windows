using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Application.Tests.Fakes;

public sealed class FakeClipboardService : IClipboardService
{
    public string? Text { get; set; }

    public Result SetTextResult { get; set; } = Result.Success();

    public string? LastSetText { get; private set; }

    public int GetCallCount { get; private set; }

    public int SetCallCount { get; private set; }

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetCallCount++;
        return Task.FromResult(Text);
    }

    public Task<Result> SetTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetCallCount++;
        LastSetText = text;
        return Task.FromResult(SetTextResult);
    }
}
