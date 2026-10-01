using System.Text;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents;

/// <summary>
/// Decides whether something somebody typed was meant as a request to the agent.
/// <para>
/// The agent is opt-in from chat and voice, and the bar for that is deliberately high. Chat
/// already answers questions well by streaming, and an assistant that quietly decides a
/// sentence needs a plan, a registry walk, and an approval prompt is worse than one that
/// simply answers. So this class recognises two things and nothing else: an explicit
/// <c>agent:</c> prefix, and a named demonstration.
/// </para>
/// <para>
/// A demonstration is only matched when the word "demonstration" or "demo" is also present.
/// That extra requirement is what stops the ordinary sentence "create a report" from being read
/// as a request for the report demonstration — which matters, because the scenario identifiers
/// are ordinary English words and would otherwise be matched by any sentence containing one.
/// </para>
/// <para>
/// The rules are fixed text matching rather than intent detection, deliberately. This decides
/// whether to hand something to the agent at all, and a wrong "yes" here sends a conversation
/// down a path it did not ask for, so it should be possible to read every rule in this file and
/// be certain of what it accepts. Ambiguity is resolved by not matching.
/// </para>
/// </summary>
public static class AgentRequestTrigger
{
    /// <summary>
    /// Gets the prefix that hands the rest of a message to the agent.
    /// </summary>
    /// <remarks>
    /// A colon rather than a word like "agent" on its own, because a bare word would have to be
    /// distinguished from every sentence that happens to mention one, and a punctuation mark is
    /// unambiguous in a way a word never is.
    /// </remarks>
    public const string Prefix = "agent:";

    private static readonly string[] DemonstrationWords =
    [
        "demonstration",
        "demonstrations",
        "demo",
        "demos",
    ];

    /// <summary>
    /// Attempts to read an agent request from text typed or spoken by a person.
    /// </summary>
    /// <param name="text">What the person said. May be <see langword="null"/>.</param>
    /// <param name="source">Where it came from, carried onto the request.</param>
    /// <param name="scenarios">The demonstrations that may be named.</param>
    /// <param name="context">The request to hand to the agent, when one was recognised.</param>
    /// <returns>
    /// <see langword="true"/> when the text named the agent or one of its demonstrations.
    /// </returns>
    public static bool TryMatch(
        string? text,
        AgentRequestSource source,
        IReadOnlyList<DemoScenario> scenarios,
        out AgentRequestContext context)
    {
        context = null!;
        ArgumentNullException.ThrowIfNull(scenarios);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        if (StartsWithPrefix(trimmed, out var remainder))
        {
            // A prefix with nothing after it is a person who typed the word and changed their
            // mind, not a request. Matching it would hand the agent an empty question.
            if (string.IsNullOrWhiteSpace(remainder))
            {
                return false;
            }

            context = source == AgentRequestSource.Voice
                ? AgentRequestContext.Spoken(remainder)
                : AgentRequestContext.Typed(remainder);

            return true;
        }

        var scenario = MatchDemonstration(trimmed, scenarios);

        if (scenario is not null)
        {
            context = AgentRequestContext.Demo(scenario);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to read an agent request from text typed or spoken by a person, naming the
    /// built-in demonstrations.
    /// </summary>
    public static bool TryMatch(
        string? text,
        AgentRequestSource source,
        out AgentRequestContext context) =>
        TryMatch(text, source, AgentDemoScenarios.All, out context);

    /// <summary>
    /// Finds a demonstration the person named, but only if they also said it was one.
    /// </summary>
    /// <param name="text">The trimmed message.</param>
    /// <param name="scenarios">The demonstrations that may be named.</param>
    /// <returns>The scenario, or <see langword="null"/> when none was named as a demonstration.</returns>
    public static DemoScenario? MatchDemonstration(string? text, IReadOnlyList<DemoScenario> scenarios)
    {
        ArgumentNullException.ThrowIfNull(scenarios);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = Normalize(text);

        var namesADemonstration = DemonstrationWords.Any(word => ContainsPhrase(normalized, word));

        if (!namesADemonstration)
        {
            return null;
        }

        // The word is required before the name is considered, so an ordinary sentence that
        // happens to contain a scenario identifier is not a demonstration request.
        return scenarios.FirstOrDefault(scenario =>
            ContainsPhrase(normalized, Normalize(scenario.Id)));
    }

    /// <summary>
    /// Detects the prefix, allowing for the space or tab somebody's keyboard puts in after a
    /// colon.
    /// </summary>
    private static bool StartsWithPrefix(string text, out string remainder)
    {
        remainder = string.Empty;

        if (text.Length < Prefix.Length ||
            !text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        remainder = text[Prefix.Length..].TrimStart();
        return true;
    }

    /// <summary>
    /// Reduces text to a comparable form: lower case, single spaces, with hyphens and
    /// underscores read as the spaces a person would type instead.
    /// </summary>
    private static string Normalize(string value)
    {
        var lowered = value.Trim().ToLowerInvariant();
        var builder = new StringBuilder(lowered.Length);

        foreach (var character in lowered)
        {
            if (character is '-' or '_')
            {
                builder.Append(' ');
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                continue;
            }

            // Punctuation at the edges is dropped so "run the demo." reads the same as
            // "run the demo", but interior punctuation is kept so it cannot accidentally join
            // two words into a phrase that was never written.
            if (char.IsPunctuation(character) || char.IsSymbol(character))
            {
                if (builder.Length == 0)
                {
                    continue;
                }

                builder.Append(' ');
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Reports whether a phrase appears in the text as whole words, so that a search for
    /// "report" does not match "reported".
    /// </summary>
    private static bool ContainsPhrase(string haystack, string phrase)
    {
        if (phrase.Length == 0 || haystack.Length < phrase.Length)
        {
            return false;
        }

        var index = haystack.IndexOf(phrase, StringComparison.Ordinal);

        while (index >= 0)
        {
            var startsOnAWord = index == 0 || haystack[index - 1] == ' ';
            var end = index + phrase.Length;
            var endsOnAWord = end == haystack.Length || haystack[end] == ' ';

            if (startsOnAWord && endsOnAWord)
            {
                return true;
            }

            index = haystack.IndexOf(phrase, index + 1, StringComparison.Ordinal);
        }

        return false;
    }
}
