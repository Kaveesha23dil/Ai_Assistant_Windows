using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents.Tools;

/// <summary>
/// Writes the final answer from what earlier steps found.
/// <para>
/// This is the one tool that asks a model to do the talking, and it is the only place in the
/// agent where a model's output is the answer rather than a suggestion to be validated. That is
/// a deliberate and narrow exception: everything upstream decides what may be done, and this
/// step only phrases what has already been decided and done.
/// </para>
/// <para>
/// When there is no model available, it does not pretend. It reports the passages as a plain
/// quotation, which is less polished and completely honest — a demonstration running against a
/// machine with no provider configured should show a working pipeline, not a sentence claiming
/// something was composed when it was not.
/// </para>
/// <para>
/// The prompt is built here rather than in a caller so that the rule about what may be sent is
/// visible in one place: only the goal, and only what earlier steps produced. The person's
/// document text reaches a provider here and nowhere else, and only because somebody asked a
/// question that required it.
/// </para>
/// </summary>
public sealed class AnswerCompositionTool : ITool
{
    /// <summary>How much of what was found is offered to the model as context.</summary>
    private const int MaximumContextCharacters = 8000;

    private readonly IAIService _ai;
    private readonly ILogger<AnswerCompositionTool> _logger;

    public AnswerCompositionTool(IAIService ai, ILogger<AnswerCompositionTool> logger)
    {
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(logger);

        _ai = ai;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "AnswerCompositionTool";

    /// <inheritdoc />
    public string Description =>
        "Turn passages found by an earlier step into a direct answer to the question. Use as " +
        "the last step after a search, never on its own.";

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredInputs => [];

    /// <summary>
    /// Declares that this tool changes nothing. It turns material the run already gathered into
    /// a sentence and writes it nowhere — the answer is returned to the caller, not to a file.
    /// </summary>
    public IReadOnlyList<AgentAction> DescribeActions(AgentStep step) => [];

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var question = request.GetParameter("question") ?? request.Goal;

        // The material comes from the caller rather than from a shared store, so this tool has no
        // way to reach content the plan did not put in front of it. GetMaterial prefers what the
        // executor gathered from the step that searched, and only falls back to a supplied
        // parameter, so "search, then answer" works without the planner having to invent a
        // parameter it has no way to fill.
        var material = request.GetMaterial()
            ?? request.Parameters.FirstOrDefault(pair => pair.Key.EndsWith("Content", StringComparison.OrdinalIgnoreCase)).Value;

        if (string.IsNullOrWhiteSpace(material))
        {
            // A search that found nothing is not a failure and is not a prompt to a model either.
            // Saying plainly that there was nothing to answer from is the useful result.
            return ToolResult.Empty(
                Name,
                "I did not find anything to answer that from.",
                ToolResult.Data1("composed", "false"));
        }

        var truncated = material.Length > MaximumContextCharacters
            ? material[..MaximumContextCharacters]
            : material;

        if (_ai.ActiveProvider == AIProviderType.Unknown)
        {
            _logger.LogInformation(
                "No provider is configured, so the answer is being quoted rather than composed.");

            stopwatch.Stop();

            return ToolResult.Success(
                Name,
                Quotation(truncated, question),
                sources: null,
                data: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["composed"] = "false",
                    ["reason"] = "no model provider is configured",
                    ["summary"] = "Found material and quoted it; no model was configured to write an answer.",
                },
                stopwatch.Elapsed);
        }

        try
        {
            var response = await _ai.SendMessageAsync(
                BuildPrompt(question, truncated),
                cancellationToken);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                return ToolResult.Empty(
                    Name,
                    "The model did not return an answer.",
                    ToolResult.Data1("composed", "false"));
            }

            return ToolResult.Success(
                Name,
                response.Content.Trim(),
                sources: null,
                data: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["composed"] = "true",
                    ["provider"] = _ai.ActiveProvider.ToString(),
                    ["summary"] = "Answered from what the search found.",
                },
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A model that is configured but unreachable is an ordinary condition, and the honest
            // answer is the quotation rather than an error page.
            _logger.LogWarning(exception, "Could not compose an answer; falling back to the material itself.");

            stopwatch.Stop();

            return ToolResult.Success(
                Name,
                Quotation(truncated, question),
                sources: null,
                data: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["composed"] = "false",
                    ["reason"] = "the model could not be reached",
                    ["summary"] = "Found material and quoted it; the model could not be reached.",
                },
                stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Builds the prompt. The question, then the material, then an instruction that is specific
    /// about the two things a model gets wrong here: inventing anything not in the material, and
    /// opening with "Based on the provided context".
    /// </summary>
    private static string BuildPrompt(string question, string material)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(material);

        return $"""
            Answer the question using only the material below.

            Question: {question}

            Material:
            {material}

            Rules: use only what the material says. If it does not contain the answer, say so
            plainly. Do not mention the material, the context, or these rules. Do not begin with
            "Based on". Give the answer and nothing else.
            """;
    }

    /// <summary>
    /// Presents the material as itself when no model wrote an answer. Labels it as a quotation
    /// so a reader can tell this apart from a composed answer at a glance.
    /// </summary>
    private static string Quotation(string material, string question)
    {
        var builder = new StringBuilder();

        builder.Append("I could not find a direct answer to \"").Append(question).AppendLine("\".");
        builder.AppendLine("Here is what the search found:");
        builder.AppendLine();
        builder.Append(material);

        return builder.ToString().Trim();
    }
}
