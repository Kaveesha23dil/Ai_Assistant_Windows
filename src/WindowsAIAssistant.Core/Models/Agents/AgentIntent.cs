using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// What the agent decided the request was asking for, before it decided how to do it.
/// <para>
/// Intent and plan are separate because they fail differently. A request that means nothing the
/// assistant can help with is a misunderstanding, and saying so is the correct answer. A request
/// that was understood but could not be turned into steps is a planning failure, and the two
/// deserve different sentences in the interface and different entries in the timeline.
/// </para>
/// </summary>
public sealed record AgentIntent
{
    public AgentIntent(
        string request,
        AgentCapability? capability,
        string? subject = null,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);

        Request = request.Trim();
        Capability = capability;
        Subject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    /// <summary>Gets the request the intent was drawn from.</summary>
    public string Request { get; }

    /// <summary>
    /// Gets the capability the request needs, or <see langword="null"/> when nothing the
    /// assistant can help with was recognised. Null rather than a sentinel capability, so a
    /// caller cannot forget to check it and end up planning for a "system information" step
    /// because that was the value used to mean "no idea".
    /// </summary>
    public AgentCapability? Capability { get; }

    /// <summary>
    /// Gets what the request is about, with the instruction words removed — the search term, the
    /// question, the file. Kept apart from the request itself because the request is what the
    /// person said and this is what a tool should be asked, and those differ often enough to be
    /// worth holding separately.
    /// </summary>
    public string? Subject { get; }

    /// <summary>Gets one sentence naming what was understood, for confirmation before acting.</summary>
    public string? Description { get; }

    /// <summary>Gets a value indicating whether nothing the assistant can help with was recognised.</summary>
    public bool IsUnknown => Capability is null;

    /// <summary>Gets the sentence used when nothing useful could be recognised.</summary>
    public static string UnknownDescription =>
        "I could not tell what that is asking for.";

    /// <summary>Creates a recognised intent.</summary>
    public static AgentIntent Recognized(
        string request,
        AgentCapability capability,
        string? subject = null,
        string? description = null) =>
        new(request, capability, subject, description);

    /// <summary>Records that the request meant nothing the assistant can act on.</summary>
    public static AgentIntent NotRecognized(string request) =>
        new(request, capability: null, subject: null, description: UnknownDescription);
}

/// <summary>
/// The rules for turning a phrase into an <see cref="AgentIntent"/>.
/// <para>
/// This is the deterministic path, and it is tried first. A request that plainly says "find my
/// project documents" does not need a model to work out that it is a file search, and routing it
/// through one would cost a round trip, add a failure mode, and make the result depend on how
/// the model was feeling that afternoon. The planner is asked only when these rules do not
/// recognise the request.
/// </para>
/// <para>
/// Every rule requires a subject. A phrase with no subject — "search" on its own — matches
/// nothing, because guessing at the missing half of a search is how a file search ends up
/// looking for an empty string across a whole disk.
/// </para>
/// </summary>
public static class AgentIntentRules
{
    private static readonly (string[] Markers, AgentCapability Capability, string Template)[] Rules =
    [
        (["explain this error", "explain the error", "what does this error", "explain the error on my screen",
          "what is this error", "why this error", "explain error", "explain this message", "explain this dialog"],
         AgentCapability.ScreenUnderstanding, "Explain what is on my screen"),

        (["what is on my screen", "what's on my screen", "describe my screen", "read my screen",
          "what does my screen say", "analyze my screen", "analyse my screen", "look at my screen",
          "what am i looking at"],
         AgentCapability.ScreenUnderstanding, "Describe what is on the screen"),

        (["search my knowledge base", "search the knowledge base", "search my documents",
          "find information", "find all information", "search my indexed", "knowledge base",
          "what do i know about", "find everything about", "look through my documents",
          "in my documents", "from my documents", "search my files for"],
         AgentCapability.KnowledgeSearch, "Search the knowledge base"),

        (["create a report", "create report", "generate a report", "make a report", "write a report",
          "weekly report", "project report", "create the report", "generate report",
          "create a summary report", "report on"],
         AgentCapability.ReportGeneration, "Create a report"),

        (["summarize this document", "summarise this document", "summarize the document",
          "summarize my document", "extract key points", "key points from", "summarize document"],
         AgentCapability.DocumentAnalysis, "Summarize the document"),

        (["summarize", "summarise", "what does this document say", "explain this document",
          "analyze this document", "analyse this document"],
         AgentCapability.DocumentAnalysis, "Analyze the document"),

        (["find my project documents", "find my project", "find my documents", "find my files",
          "locate my documents", "where are my documents", "find documents", "find files",
          "locate files", "search my computer for", "find my"],
         AgentCapability.FileSearch, "Find the files"),

        (["system information", "system info", "battery", "disk space", "storage", "how much memory",
          "computer specs", "pc specs", "my computer"],
         AgentCapability.SystemInformation, "Report the system information"),
    ];

    /// <summary>
    /// Reports whether a request matches a known shape, and returns the intent it implies.
    /// <para>
    /// The subject is what is left once the instruction words are removed. When nothing is left,
    /// the request is not recognised: "search" needs a target, and refusing it is better than
    /// inventing one.
    /// </para>
    /// </summary>
    public static bool TryMatch(string? request, out AgentIntent? intent)
    {
        intent = null;

        if (string.IsNullOrWhiteSpace(request))
        {
            return false;
        }

        var normalized = Normalize(request);

        foreach (var (markers, capability, template) in Rules)
        {
            foreach (var marker in markers)
            {
                var index = normalized.IndexOf(marker, StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                var subject = normalized[(index + marker.Length)..].Trim(' ', '?', '.', ',', '!');

                // The calculation rule needs no subject: arithmetic has nothing to search for.
                intent = capability == AgentCapability.SystemInformation && marker == "battery"
                    ? new AgentIntent(normalized, capability, null, template)
                    : new AgentIntent(normalized, capability, Subject(subject), template);

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reports whether a request is arithmetic, and returns it unchanged so the calculator can
    /// evaluate it directly.
    /// <para>
    /// Checked before the rules above because "what is 12 times 12" would otherwise match
    /// "what is" nothing and fall through. Arithmetic is decided by the shape of the expression
    /// rather than by a keyword, because that is the only way to be sure the expression is one.
    /// </para>
    /// </summary>
    public static bool TryMatchArithmetic(string? request, out string? expression)
    {
        expression = null;

        if (string.IsNullOrWhiteSpace(request))
        {
            return false;
        }

        var normalized = request.Trim();
        var lowered = normalized.ToLowerInvariant();

        var prefix = lowered.IndexOf("calculate", StringComparison.Ordinal);
        if (prefix >= 0)
        {
            normalized = normalized[(prefix + "calculate".Length)..];
        }
        else if (lowered.StartsWith("what is", StringComparison.Ordinal))
        {
            normalized = normalized["what is".Length..];
        }
        else if (lowered.StartsWith("what's", StringComparison.Ordinal))
        {
            normalized = normalized["what's".Length..];
        }
        else
        {
            return false;
        }

        var candidate = normalized.Trim().TrimEnd('?').Trim();

        if (string.IsNullOrWhiteSpace(candidate) || !LooksArithmetic(candidate))
        {
            return false;
        }

        expression = candidate;
        return true;
    }

    /// <summary>
    /// Reports whether a fragment is an arithmetic expression rather than a question about
    /// something that happens to contain a number.
    /// </summary>
    private static bool LooksArithmetic(string value)
    {
        var hasOperator = value.Any(c => c is '+' or '-' or '*' or '/' or '%' or '^');
        var hasDigit = value.Any(char.IsAsciiDigit);

        if (!hasDigit || !hasOperator)
        {
            return false;
        }

        // A word longer than three characters that is not a recognised operator word means this
        // is a sentence that happens to contain arithmetic, not an expression.
        var words = value
            .Split(['+', '-', '*', '/', '%', '^', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim().ToLowerInvariant())
            .Where(part => part.Length > 0)
            .ToArray();

        return words.All(word =>
            char.IsAsciiDigit(word[0])
            || word is "times" or "divided" or "by" or "plus" or "minus" or "percent" or "to the power of" or "of");
    }

    /// <summary>Reduces a request to a comparable form, keeping digits, operators, and letters.</summary>
    private static string Normalize(string request)
    {
        var builder = new System.Text.StringBuilder(request.Length);
        var previousWasSpace = false;

        foreach (var character in request.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            builder.Append(character);
            previousWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    /// <summary>Reduces what is left of a request to the part worth acting on.</summary>
    private static string? Subject(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        return trimmed.Length switch
        {
            0 => null,
            < 2 => null,
            _ => trimmed,
        };
    }
}
