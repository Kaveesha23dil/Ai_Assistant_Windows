using System.Text.Json;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents;

/// <summary>
/// Turns a request into a plan, by rule first and by model only when the rules do not
/// recognise it.
/// <para>
/// The order is the whole design. A request that plainly says "find my project documents" is
/// routed by a rule, so the same words always produce the same plan with no provider call, no
/// latency, and no dependence on how a model was answering that afternoon — and the
/// demonstration mode behaves identically to a typed request because there is nothing else in
/// the path to behave differently. The model is asked only for the requests the rules cannot
/// place, and whatever it returns is put through the same validation as everything else.
/// </para>
/// <para>
/// The model is never trusted with what to execute. It is asked for JSON, and every field of
/// that JSON is checked: the tool must be one the registry knows, the required inputs must be
/// present, and the plan must not be empty. A model that answers in prose, or that invents a
/// tool, gets a refusal rather than an approximation — running the nearest available tool instead
/// would be doing something nobody asked for, and would be doing it on a machine that acts.
/// </para>
/// </summary>
public sealed class AgentPlanner : IAgentPlanner
{
    private readonly IToolRegistry _tools;
    private readonly IAIService _aiService;
    private readonly ILogger<AgentPlanner> _logger;

    public AgentPlanner(
        IToolRegistry tools,
        IAIService aiService,
        ILogger<AgentPlanner> logger)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(aiService);
        ArgumentNullException.ThrowIfNull(logger);

        _tools = tools;
        _aiService = aiService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<AgentPlan>> CreatePlanAsync(
        AgentRequestContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runId = Guid.NewGuid();

        // Arithmetic first, ahead of every rule. "What is 12 times 12" would otherwise be
        // caught by nothing and asked to a model, and a model asked to do sums will do sums
        // approximately. This path is exact and local.
        if (AgentIntentRules.TryMatchArithmetic(context.Request, out var expression))
        {
            return Result<AgentPlan>.Success(BuildArithmeticPlan(runId, context, expression!));
        }

        if (AgentIntentRules.TryMatch(context.Request, out var intent) && intent is not null)
        {
            var plan = BuildRulePlan(runId, context, intent);
            return plan is null
                ? Result<AgentPlan>.Failure(
                    ErrorCodes.AgentIntentUnrecognized,
                    "I understood that request, but there is no tool here that can carry it out.")
                : Result<AgentPlan>.Success(plan);
        }

        _logger.LogInformation(
            "No rule matched request {RunId} from {Source}; asking the model for a plan.",
            runId,
            context.Source);

        var generated = await GenerateWithModelAsync(runId, context, cancellationToken);
        return generated;
    }

    /// <inheritdoc />
    public async Task<string?> DescribePlanAsync(
        AgentPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!string.IsNullOrWhiteSpace(plan.Explanation))
        {
            return plan.Explanation;
        }

        // A one-step plan needs no model to be described; saying what a single step is for is
        // already in the step.
        if (plan.Steps.Count == 1)
        {
            return plan.Steps[0].Description;
        }

        try
        {
            var prompt = $"In one sentence, say what the assistant is about to do for this request: " +
                $"\"{plan.OriginalRequest}\". Steps: " +
                string.Join("; ", plan.Steps.Select(step => step.Description)) +
                ". Answer with the sentence only.";

            var response = await _aiService.SendMessageAsync(prompt, cancellationToken);

            return string.IsNullOrWhiteSpace(response.Content) ? null : response.Content.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Describing a plan is a convenience. If the model cannot do it, the plan is still
            // shown and still runs, so this is logged and swallowed rather than turned into a
            // failure that would block work which is perfectly well understood.
            _logger.LogWarning(exception, "Could not describe plan {PlanId}.", plan.Id);
            return null;
        }
    }

    /// <summary>
    /// Builds the plan for a request the rules recognised, by looking up the one tool that can
    /// carry it out.
    /// <para>
    /// Returns <see langword="null"/> when the capability is recognised but no tool is registered
    /// for it. That is a real distinction: the request was understood, and the reason it cannot
    /// be done is a missing tool, which the workspace should say rather than reporting a failure
    /// to understand.
    /// </para>
    /// </summary>
    private AgentPlan? BuildRulePlan(Guid runId, AgentRequestContext context, AgentIntent intent)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(intent);

        var (toolName, steps) = intent.Capability switch
        {
            AgentCapability.KnowledgeSearch => ("KnowledgeSearchTool", (IEnumerable<AgentStep>)[
                Search(intent),
                Answer(intent)]),

            AgentCapability.FileSearch => ("FileSearchTool", [Search(intent)]),

            AgentCapability.DocumentAnalysis => ("DocumentAnalysisTool", [Summarize(intent)]),

            AgentCapability.ScreenUnderstanding => ("ScreenAnalysisTool", [ExplainScreen(intent)]),

            AgentCapability.SystemInformation => ("SystemInformationTool", [ReportSystem(intent)]),

            AgentCapability.Calculation => ("CalculationTool", [Calculate(intent)]),

            // A report is a chain rather than a single step, because the plan that produces the
            // material and the step that writes the file are different capabilities and either
            // can fail on its own. A single "make a report" tool would have to do both, and a
            // failure to save would then be indistinguishable from a failure to find anything.
            AgentCapability.ReportGeneration => ("KnowledgeSearchTool", [
                Search(intent),
                WriteReport(intent)]),

            _ => (string.Empty, (IEnumerable<AgentStep>)[]),
        };

        if (string.IsNullOrEmpty(toolName))
        {
            return null;
        }

        var lead = steps.First();
        var catalogue = _tools.Check(toolName);

        if (catalogue == ToolAvailability.Unregistered)
        {
            _logger.LogWarning(
                "Recognised {Capability} for run {RunId} but {Tool} is not registered.",
                intent.Capability,
                runId,
                toolName);

            return null;
        }

        var resolved = steps
            .Select(step => step with { ToolName = _tools.Resolve(step.ToolName) })
            .ToArray();

        return new AgentPlan(
            runId,
            intent.Capability == AgentCapability.ReportGeneration
                ? "Create a report from your indexed documents"
                : lead.Description,
            resolved,
            DateTimeOffset.UtcNow,
            context.Request,
            context.Source,
            intent.Description);
    }

    /// <summary>
    /// Asks the model for a plan as JSON, then validates every field of the answer against the
    /// registry.
    /// <para>
    /// The answer is parsed into a private shape and never handed straight to
    /// <see cref="AgentPlan"/>. The validation below is the only way a model-produced plan comes
    /// into being, and every check it makes is a check on a name or an input rather than on the
    /// model's judgement.
    /// </para>
    /// </summary>
    private async Task<Result<AgentPlan>> GenerateWithModelAsync(
        Guid runId,
        AgentRequestContext context,
        CancellationToken cancellationToken)
    {
        var catalogue = _tools.BuildToolCatalogue();

        if (string.IsNullOrWhiteSpace(catalogue))
        {
            return Result<AgentPlan>.Failure(
                ErrorCodes.AgentToolUnavailable,
                "No tools are available on this machine, so the request cannot be carried out.");
        }

        // Four dollars, so any run of braces is literal. The JSON example ends in four closing
        // braces, which even $$ would read as an interpolation ending. With four, the two holes
        // are written as {{catalogue}} and {{context.Request}}.
        var prompt = $$$$"""
            You are planning how to answer a request using tools.

            Available tools:
            {{{{catalogue}}}}

            Request: "{{{context.Request}}}"

            Reply with JSON only, no prose and no code fence, in exactly this shape:
            {"goal": "one sentence", "capability": "one of the names above", "steps": [{"tool": "an exact tool name from the list", "description": "one sentence", "reason": "one short sentence", "parameters": {"key": "value"}}]}

            Rules: use only tool names exactly as written above. Do not invent tools. Do not
            include any step that changes a file or a setting. If nothing in the list can serve
            the request, reply with {"goal": "", "capability": "", "steps": []}.
            """;

        AIResponse response;

        try
        {
            response = await _aiService.SendMessageAsync(prompt, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The model could not be asked for a plan for run {RunId}.", runId);
            return Result<AgentPlan>.Failure(
                ErrorCodes.AgentPlanUnreadable,
                "I could not work out how to do that.");
        }

        var parsed = TryParsePlan(response.Content);

        if (parsed is null || parsed.Steps.Count == 0)
        {
            _logger.LogInformation(
                "The model returned no usable plan for run {RunId}; the request is not understood.",
                runId);

            return Result<AgentPlan>.Failure(
                ErrorCodes.AgentIntentUnrecognized,
                AgentIntent.UnknownDescription);
        }

        var steps = new List<AgentStep>();
        var rejected = new List<string>();

        foreach (var candidate in parsed.Steps)
        {
            // A step that names no tool cannot be looked up, and a step with no description cannot
            // be shown on a timeline. Both are dropped here rather than defaulted, because a
            // step with an invented description would be worse than a plan with one step fewer.
            if (string.IsNullOrWhiteSpace(candidate.Tool))
            {
                rejected.Add("a step did not name a tool");
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate.Description))
            {
                rejected.Add($"{candidate.Tool} did not say what it was for");
                continue;
            }

            var availability = _tools.Check(candidate.Tool);

            switch (availability)
            {
                case ToolAvailability.Unregistered:
                    rejected.Add($"{candidate.Tool} is not a tool this assistant has");
                    continue;

                case ToolAvailability.Unavailable:
                case ToolAvailability.Misconfigured:
                    rejected.Add(_tools.GetUnavailableReason(candidate.Tool) ?? "a tool is not available");
                    continue;
            }

            var toolName = _tools.Resolve(candidate.Tool);
            var missing = _tools.GetMissingInputs(toolName, candidate.Parameters);

            if (missing.Count > 0)
            {
                rejected.Add($"{toolName} needs {string.Join(" and ", missing)}");
                continue;
            }

            steps.Add(AgentStep.Create(
                toolName,
                candidate.Description,
                candidate.Reason,
                candidate.Parameters));
        }

        if (steps.Count == 0)
        {
            var detail = rejected.Count > 0 ? $" {string.Join("; ", rejected)}." : string.Empty;
            return Result<AgentPlan>.Failure(
                ErrorCodes.AgentToolUnknown,
                $"I could not find a way to do that.{detail}");
        }

        if (rejected.Count > 0)
        {
            // A partially usable plan is worth having. The steps that cannot run are dropped and
            // said out loud, rather than failing the whole request over a step the request did
            // not strictly need.
            _logger.LogInformation(
                "Dropped {Count} unusable step(s) from run {RunId}: {Detail}",
                rejected.Count,
                runId,
                string.Join("; ", rejected));
        }

        return Result<AgentPlan>.Success(new AgentPlan(
            runId,
            parsed.Goal ?? steps[0].Description,
            steps,
            DateTimeOffset.UtcNow,
            context.Request,
            context.Source,
            parsed.Goal));
    }

    /// <summary>Builds the single-step plan for an arithmetic request.</summary>
    private static AgentPlan BuildArithmeticPlan(
        Guid runId,
        AgentRequestContext context,
        string expression)
    {
        var step = AgentStep.Create(
            "CalculationTool",
            $"Work out {expression}",
            "Arithmetic is done locally and exactly rather than sent to a model.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["expression"] = expression,
            });

        return new AgentPlan(
            runId,
            $"Work out {expression}",
            [step],
            DateTimeOffset.UtcNow,
            context.Request,
            context.Source,
            $"Work out {expression}");
    }

    private static AgentStep Search(AgentIntent intent) => AgentStep.Create(
        "KnowledgeSearchTool",
        $"Search your indexed documents for \"{intent.Subject ?? intent.Request}\"",
        "The answer has to come from what has been indexed, so this runs first.",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["query"] = intent.Subject ?? intent.Request,
        });

    private static AgentStep Answer(AgentIntent intent) => AgentStep.Create(
        "AnswerCompositionTool",
        "Write the answer from what the search found",
        "Composition needs the search to have finished, so it reads the previous step's result.",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["question"] = intent.Subject ?? intent.Request,
        });

    private static AgentStep Summarize(AgentIntent intent) => AgentStep.Create(
        "DocumentAnalysisTool",
        "Read the document and summarize it",
        "The request names a document rather than the index, so it is read directly.",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["request"] = intent.Subject ?? intent.Request,
        });

    private static AgentStep ExplainScreen(AgentIntent intent) => AgentStep.Create(
        "ScreenAnalysisTool",
        "Look at the screen and explain what it says",
        "The request is about what is on the screen right now, so the screen has to be read.",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["request"] = intent.Subject ?? intent.Request,
        });

    private static AgentStep ReportSystem(AgentIntent intent) => AgentStep.Create(
        "SystemInformationTool",
        intent.Description ?? "Report what this computer is and how it is doing",
        "This is answered by the operating system rather than by a model.",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["request"] = intent.Subject ?? intent.Request,
        });

    private static AgentStep Calculate(AgentIntent intent) => AgentStep.Create(
        "CalculationTool",
        $"Work out {intent.Subject ?? intent.Request}",
        "Arithmetic is done locally and exactly rather than sent to a model.",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["expression"] = intent.Subject ?? intent.Request,
        });

    private static AgentStep WriteReport(AgentIntent intent) => AgentStep.Create(
        "ReportGenerationTool",
        "Write the report to a file",
        "Writing the file is a separate step so that a failure to save is reported on its own.",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = intent.Subject ?? "Report",
        });

    /// <summary>
    /// Reads the model's answer as JSON, tolerating the two things models actually do: wrapping
    /// it in a code fence, and adding a sentence in front.
    /// <para>
    /// Returns <see langword="null"/> for anything that is not a plan. Guessing at a malformed
    /// answer would mean inventing steps nobody asked for, so a bad answer is a refusal and the
    /// deterministic path above is the safety net.
    /// </para>
    /// </summary>
    private static GeneratedPlan? TryParsePlan(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var text = content.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n', StringComparison.Ordinal);
            var closingFence = text.LastIndexOf("```", StringComparison.Ordinal);

            if (firstNewline >= 0 && closingFence > firstNewline)
            {
                text = text[(firstNewline + 1)..closingFence].Trim();
            }
        }

        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GeneratedPlan>(text[start..(end + 1)], JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The shape asked of the model, and the only shape a model answer is read into.</summary>
    private sealed record GeneratedPlan
    {
        public string? Goal { get; init; }

        public string? Capability { get; init; }

        public List<GeneratedStep> Steps { get; init; } = [];
    }

    /// <summary>One step as the model proposed it, before any of it has been checked.</summary>
    private sealed record GeneratedStep
    {
        public string? Tool { get; init; }

        public string? Description { get; init; }

        public string? Reason { get; init; }

        public Dictionary<string, string> Parameters { get; init; } = [];
    }
}
