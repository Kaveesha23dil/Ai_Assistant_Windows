using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.Vision;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;
using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Application.Agents.Tools;

/// <summary>
/// Captures the screen and explains what is on it.
/// <para>
/// This is the one tool in the set that reaches a surface the person was not consciously sharing:
/// a screen holds whatever was open, including things that had nothing to do with the question.
/// It therefore names its consent switch, so the executor checks the same switch the page and
/// the voice path check, and a request arriving through the agent cannot get past a setting that
/// applies everywhere else.
/// </para>
/// <para>
/// It captures the screen but does not save an image. The frame is handed to the analysis service
/// and released; the tool returns text. A tool that wrote a screenshot to disk as a side effect
/// would leave a picture of somebody's screen in a folder they did not know about, which is the
/// one outcome this whole feature is built to avoid.
/// </para>
/// <para>
/// Read-only as far as the machine is concerned, so it declares no actions and is not held at
/// the approval gate. The consent switch is the gate here, and adding a second one would mean a
/// dialog every time somebody asks what is on their screen — which is the request they made.
/// </para>
/// </summary>
public sealed class ScreenAnalysisTool : ITool
{
    private readonly IScreenAnalysisService _screen;
    private readonly ILogger<ScreenAnalysisTool> _logger;

    public ScreenAnalysisTool(IScreenAnalysisService screen, ILogger<ScreenAnalysisTool> logger)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(logger);

        _screen = screen;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "ScreenAnalysisTool";

    /// <inheritdoc />
    public string Description =>
        "Look at what is on the screen right now and explain it, including reading an error " +
        "message, a dialog, or a chart. Use this only for questions about the current screen.";

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredInputs => [];

    /// <summary>
    /// Declares that this tool changes nothing on the machine. It takes a picture of what is
    /// already on screen and explains it.
    /// <para>
    /// Worth being precise about, because it is the case most likely to be argued with. Taking
    /// the capture is not the action; the actions are the things a person cannot see from a
    /// screenshot being taken — the file being kept afterwards, and the picture leaving the
    /// machine. Those are the switch declared above and the history switch it is checked
    /// against, and a dialog here would be asking permission to look at a screen the person is
    /// already looking at.
    /// </para>
    /// </summary>
    public IReadOnlyList<AgentAction> DescribeActions(AgentStep step) => [];

    /// <inheritdoc />
    public PermissionCapability? RequiredPermission => PermissionCapability.ScreenAnalysis;

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var question = request.GetParameter("request") ?? request.Goal;

        try
        {
            var result = await _screen.AnalyzeScreenAsync(
                new ScreenAnalysisRequestOptions(
                    ChooseType(question),
                    userQuestion: question),
                cancellationToken);

            stopwatch.Stop();

            if (result.IsFailure || result.Value is null)
            {
                // The service's own message, when it has one. It knows which specific thing went
                // wrong — no capture, no OCR, no model that accepts images — and a generic
                // sentence here would throw that away.
                return ToolResult.Failure(
                    Name,
                    result.ErrorCode ?? ErrorCodes.AgentToolUnavailable,
                    result.ErrorMessage ?? "I could not look at the screen just now.");
            }

            var analysis = result.Value;
            var text = analysis.BuildDisplayText();

            if (string.IsNullOrWhiteSpace(text))
            {
                return ToolResult.Empty(
                    Name,
                    "I could not read anything from the screen.",
                    ToolResult.Data1("summary", "Screen was blank or unreadable."));
            }

            return ToolResult.Success(
                Name,
                text,
                sources: null,
                BuildData(analysis, question),
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Screen analysis failed.");

            return ToolResult.Failure(
                Name,
                ErrorCodes.AgentStepFailed,
                "I could not look at the screen just now.");
        }
    }

    /// <summary>
    /// Chooses what kind of analysis to run from the words in the request, rather than always
    /// describing. Somebody asking "explain this error" wants the error explained, and the
    /// service has a mode for exactly that which produces a better answer than a general
    /// description of a dialog.
    /// </summary>
    private static ScreenAnalysisType ChooseType(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return ScreenAnalysisType.Describe;
        }

        var lowered = question.ToLowerInvariant();

        if (lowered.Contains("error", StringComparison.Ordinal)
            || lowered.Contains("exception", StringComparison.Ordinal)
            || lowered.Contains("failed", StringComparison.Ordinal)
            || lowered.Contains("crash", StringComparison.Ordinal))
        {
            return ScreenAnalysisType.ExplainError;
        }

        if (lowered.Contains("chart", StringComparison.Ordinal)
            || lowered.Contains("graph", StringComparison.Ordinal))
        {
            return ScreenAnalysisType.AnalyzeChart;
        }

        if (lowered.Contains("code", StringComparison.Ordinal)
            || lowered.Contains("stack trace", StringComparison.Ordinal))
        {
            return ScreenAnalysisType.AnalyzeCode;
        }

        if (lowered.Contains("button", StringComparison.Ordinal)
            || lowered.Contains("dialog", StringComparison.Ordinal)
            || lowered.Contains("window", StringComparison.Ordinal))
        {
            return ScreenAnalysisType.ExplainUI;
        }

        if (lowered.Contains("text", StringComparison.Ordinal)
            || lowered.Contains("read", StringComparison.Ordinal)
            || lowered.Contains("says", StringComparison.Ordinal))
        {
            return ScreenAnalysisType.ExtractText;
        }

        return ScreenAnalysisType.Describe;
    }

    /// <summary>
    /// Builds the metadata, which records what was analysed and where the answer came from — and
    /// deliberately records that no text or image was kept.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildData(ScreenAnalysisResult analysis, string? question)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["provider"] = analysis.Provider ?? "unknown",
            ["warningCount"] = analysis.Warnings.Count.ToString(CultureInfo.InvariantCulture),
            ["retained"] = "no image or screen text was saved",
            ["summary"] = analysis.DetectedError is null
                ? "Looked at the screen and read what was there."
                : "Read an error from the screen.",
        };

        if (!string.IsNullOrWhiteSpace(question))
        {
            data["question"] = question.Length <= 80 ? question : question[..79] + "\u2026";
        }

        // The count of warnings is kept, not their text: a warning is often "this looks like a
        // password field", and naming the field would defeat the point of the filter.
        return data;
    }
}
