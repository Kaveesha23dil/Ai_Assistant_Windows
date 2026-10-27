using System;
using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Abstractions.Computer;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.Application.Clipboard;

public sealed class ClipboardCommandService
{
    private readonly IClipboardService _clipboardService;
    private readonly IClipboardContentAnalyzer _analyzer;
    private readonly IWritingAssistantService _writingAssistant;
    private readonly IClipboardHistoryService _historyService;
    private readonly IComputerControlService? _computerControlService;

    public ClipboardCommandService(
        IClipboardService clipboardService,
        IClipboardContentAnalyzer analyzer,
        IWritingAssistantService writingAssistant,
        IClipboardHistoryService historyService,
        IComputerControlService? computerControlService = null)
    {
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _writingAssistant = writingAssistant ?? throw new ArgumentNullException(nameof(writingAssistant));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _computerControlService = computerControlService;
    }

    public async Task<ClipboardContent> ReadAsync(CancellationToken cancellationToken = default)
    {
        var content = await _clipboardService.GetCurrentContentAsync(cancellationToken).ConfigureAwait(false);
        return await _analyzer.AnalyzeAsync(content, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClipboardTransformationResult> TransformAsync(ClipboardTransformationRequest request, CancellationToken cancellationToken = default)
    {
        if (request?.Content != null)
        {
            request.Content = await _analyzer.AnalyzeAsync(request.Content, cancellationToken).ConfigureAwait(false);
        }
        return await _writingAssistant.TransformAsync(request!, cancellationToken).ConfigureAwait(false);
    }

    public async Task CopyResultAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        await _clipboardService.WriteTextAsync(text, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceClipboardAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        // Save current for undo if enabled
        var current = await _clipboardService.GetCurrentContentAsync(cancellationToken).ConfigureAwait(false);
        if (_historyService.IsEnabled)
        {
            await _historyService.SaveForUndoAsync(current, cancellationToken).ConfigureAwait(false);
        }
        await _clipboardService.WriteTextAsync(text, cancellationToken).ConfigureAwait(false);
    }

    public async Task UndoAsync(CancellationToken cancellationToken = default)
    {
        if (_historyService.IsEnabled)
        {
            await _historyService.UndoLastReplacementAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
