using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Agents;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.Application.Clipboard;

public sealed class ClipboardTool : ITool
{
    private readonly IClipboardService _clipboardService;
    private readonly IClipboardContentAnalyzer _analyzer;
    private readonly IWritingAssistantService _writingAssistant;
    private readonly IClipboardHistoryService _historyService;
    private readonly IAIService? _aiService;

    public ClipboardTool(
        IClipboardService clipboardService,
        IClipboardContentAnalyzer analyzer,
        IWritingAssistantService writingAssistant,
        IClipboardHistoryService historyService,
        IAIService? aiService = null)
    {
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _writingAssistant = writingAssistant ?? throw new ArgumentNullException(nameof(writingAssistant));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _aiService = aiService;
    }

    public string Name => "clipboard";

    public string Description => "Read clipboard, analyze content, transform text, copy results. Does not auto-replace without explicit permission.";

    public IReadOnlyList<string> RequiredInputs => new List<string>();

    public IReadOnlyList<AgentAction> DescribeActions(AgentStep step)
    {
        return Array.Empty<AgentAction>();
    }

    public async Task<ToolResult> ExecuteAsync(ToolRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return ToolResult.Failure(Name, "InvalidRequest", "Parameters are required.");
        }

        var op = request.Parameters.TryGetValue("operation", out var opObj) ? opObj ?? string.Empty : string.Empty;

        try
        {
            if (string.Equals(op, "ReadClipboard", StringComparison.OrdinalIgnoreCase))
            {
                var content = await _clipboardService.GetCurrentContentAsync(cancellationToken).ConfigureAwait(false);
                content = await _analyzer.AnalyzeAsync(content, cancellationToken).ConfigureAwait(false);
                return ToolResult.Success(Name, $"type={content.Type}; length={content.Length}; hasText={content.HasText}");
            }

            if (string.Equals(op, "AnalyzeClipboard", StringComparison.OrdinalIgnoreCase))
            {
                var content = await _clipboardService.GetCurrentContentAsync(cancellationToken).ConfigureAwait(false);
                content = await _analyzer.AnalyzeAsync(content, cancellationToken).ConfigureAwait(false);
                var actions = string.Join(", ", _writingAssistant.GetAvailableActions(content.Type).Select(a => a.Label));
                return ToolResult.Success(Name, $"type={content.Type}; actions={actions}");
            }

            if (string.Equals(op, "TransformClipboard", StringComparison.OrdinalIgnoreCase))
            {
                var content = await _clipboardService.GetCurrentContentAsync(cancellationToken).ConfigureAwait(false);
                content = await _analyzer.AnalyzeAsync(content, cancellationToken).ConfigureAwait(false);
                var actionStr = request.Parameters.TryGetValue("action", out var act) ? act : null;
                if (!Enum.TryParse<WritingAction>(actionStr, true, out var action))
                {
                    return ToolResult.Failure(Name, "InvalidAction", "Invalid action.");
                }
                var req = new ClipboardTransformationRequest
                {
                    Content = content,
                    Action = action,
                    TargetLanguage = request.Parameters.TryGetValue("targetLanguage", out var tl) ? tl : null,
                    AllowCloud = false
                };
                var res = await _writingAssistant.TransformAsync(req, cancellationToken).ConfigureAwait(false);
                if (!res.Success)
                {
                    return ToolResult.Failure(Name, "TransformFailed", res.ErrorMessage ?? "Transformation failed.");
                }
                return ToolResult.Success(Name, res.ResultText ?? string.Empty);
            }

            if (string.Equals(op, "CopyResult", StringComparison.OrdinalIgnoreCase))
            {
                var text = request.Parameters.TryGetValue("text", out var t) ? t : null;
                if (string.IsNullOrEmpty(text))
                {
                    return ToolResult.Failure(Name, "MissingText", "Text required.");
                }
                await _clipboardService.WriteTextAsync(text, cancellationToken).ConfigureAwait(false);
                return ToolResult.Success(Name, "Copied to clipboard.");
            }

            return ToolResult.Failure(Name, "UnknownOperation", $"Unknown operation: {op}");
        }
        catch (OperationCanceledException)
        {
            return ToolResult.Failure(Name, "Cancelled", "Cancelled.");
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(Name, "Error", ex.Message);
        }
    }
}
