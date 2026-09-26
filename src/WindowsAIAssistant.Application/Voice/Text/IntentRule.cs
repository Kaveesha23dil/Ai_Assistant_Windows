using System.Globalization;
using System.Text.RegularExpressions;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Application.Voice.Text;

/// <summary>
/// One entry in the deterministic intent table: a set of alternative phrasings for a single
/// intent, the safety classification it carries, and the named capture groups that become
/// command parameters.
/// <para>
/// Declaring intent knowledge as data rather than as branches in a switch is what allows many
/// phrasings per command without one enormous method, and it keeps adding a command a
/// single-table change.
/// </para>
/// </summary>
internal sealed class IntentRule
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly Regex[] _patterns;
    private readonly IReadOnlyDictionary<string, string> _constantParameters;

    public IntentRule(
        AssistantIntent intent,
        IEnumerable<string> patterns,
        double confidence = 0.95,
        ActionSafetyLevel safetyLevel = ActionSafetyLevel.Safe,
        bool requiresConfirmation = false,
        IReadOnlyDictionary<string, string>? constantParameters = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        Intent = intent;
        Confidence = Math.Clamp(confidence, 0.0, 1.0);
        SafetyLevel = safetyLevel;
        RequiresConfirmation = requiresConfirmation;
        _patterns = patterns
            .Select(pattern => new Regex(pattern, Options, MatchTimeout))
            .ToArray();
        _constantParameters = constantParameters
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public AssistantIntent Intent { get; }

    public double Confidence { get; }

    public ActionSafetyLevel SafetyLevel { get; }

    public bool RequiresConfirmation { get; }

    /// <summary>
    /// Attempts to match a normalized transcript.
    /// </summary>
    /// <returns>
    /// The captured parameters, which may be an empty set for an intent that needs none, or
    /// <see langword="null"/> when no phrasing matched.
    /// </returns>
    public Dictionary<string, string>? Match(string normalizedText)
    {
        foreach (var pattern in _patterns)
        {
            var match = pattern.Match(normalizedText);
            if (!match.Success)
            {
                continue;
            }

            var parameters = ExtractGroups(match);
            foreach (var pair in _constantParameters)
            {
                parameters[pair.Key] = pair.Value;
            }

            return parameters;
        }

        return null;
    }

    private static Dictionary<string, string> ExtractGroups(Match match)
    {
        Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);

        foreach (Group group in match.Groups)
        {
            // Unnamed capture groups are addressed by number; only named groups carry
            // parameters, and a named group that did not participate is skipped.
            if (int.TryParse(group.Name, CultureInfo.InvariantCulture, out _)
                || !group.Success
                || string.IsNullOrWhiteSpace(group.Value))
            {
                continue;
            }

            parameters[group.Name] = group.Value.Trim();
        }

        return parameters;
    }
}
