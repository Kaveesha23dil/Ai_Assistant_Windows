using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Application.Launcher;

/// <summary>
/// Routes common launcher text without a model. The ordering is deliberate: an explicit prefix
/// wins, an arithmetic expression is recognised locally, and only an otherwise ambiguous
/// question is marked for the assistant.
/// </summary>
public sealed class LauncherIntentService : ILauncherIntentService
{
    private static readonly string[] QuestionPrefixes =
    [
        "how ", "what ", "why ", "when ", "where ", "who ", "which ",
        "can you ", "could you ", "would you ", "explain ", "summarize ",
        "summarise ", "write ", "draft ", "help me ",
    ];

    private static readonly char[] MathOperators = ['+', '-', '*', '/', '×', '÷', '^', '%'];

    /// <inheritdoc />
    public LauncherIntent Route(string text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return LauncherIntent.Universal(string.Empty);
        }

        if (trimmed[0] == '=')
        {
            return LauncherIntent.For(
                LauncherIntentType.Calculator,
                trim(trimmed[1..]),
                1);
        }

        if (trimmed[0] is '>' or '/')
        {
            return LauncherIntent.For(
                LauncherIntentType.Command,
                trim(trimmed[1..]),
                1);
        }

        if (trimmed[0] == '?')
        {
            return LauncherIntent.For(
                LauncherIntentType.AiQuestion,
                trim(trimmed[1..]),
                1);
        }

        if (TryStripPrefix(trimmed, "calculate", out var calculation)
            || TryStripPrefix(trimmed, "calc", out calculation))
        {
            return LauncherIntent.For(LauncherIntentType.Calculator, calculation, 1);
        }

        if (TryStripPrefix(trimmed, "search web", out var web)
            || TryStripPrefix(trimmed, "web", out web)
            || TryStripPrefix(trimmed, "google", out web))
        {
            return LauncherIntent.For(LauncherIntentType.WebSearch, web, 1);
        }

        if (TryStripPrefix(trimmed, "find file", out var file)
            || TryStripPrefix(trimmed, "file", out file))
        {
            return LauncherIntent.For(LauncherIntentType.File, file, 0.95);
        }

        if (TryStripPrefix(trimmed, "search my documents", out var knowledge)
            || TryStripPrefix(trimmed, "ask my documents", out knowledge)
            || TryStripPrefix(trimmed, "knowledge", out knowledge))
        {
            return LauncherIntent.For(LauncherIntentType.Knowledge, knowledge, 0.95);
        }

        if (trimmed.Equals("settings", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("open settings", StringComparison.OrdinalIgnoreCase))
        {
            return LauncherIntent.For(LauncherIntentType.Setting, trimmed, 1);
        }

        if (TryStripPrefix(trimmed, "launch", out var application)
            || TryStripPrefix(trimmed, "open", out application))
        {
            if (application.Equals("settings", StringComparison.OrdinalIgnoreCase))
            {
                return LauncherIntent.For(LauncherIntentType.Setting, application, 1);
            }

            return LauncherIntent.For(LauncherIntentType.Application, application, 0.9);
        }

        if (LooksLikeArithmetic(trimmed))
        {
            return LauncherIntent.For(LauncherIntentType.Calculator, trimmed, 0.95);
        }

        if (trimmed.EndsWith('?')
            || QuestionPrefixes.Any(prefix =>
                trimmed.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)))
        {
            return LauncherIntent.For(LauncherIntentType.AiQuestion, trimmed, 0.8);
        }

        return LauncherIntent.Universal(trimmed);

        static string trim(string value) => value.Trim();

        static bool TryStripPrefix(string value, string prefix, out string remainder)
        {
            if (value.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)
                && value.Length > prefix.Length
                && char.IsWhiteSpace(value[prefix.Length]))
            {
                remainder = value[(prefix.Length + 1)..].Trim();
                return remainder.Length > 0;
            }

            remainder = string.Empty;
            return false;
        }
    }

    private static bool LooksLikeArithmetic(string text)
    {
        if (text.Length > 200
            || !text.Any(char.IsAsciiDigit)
            || !text.Any(character => MathOperators.Contains(character)))
        {
            return false;
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiDigit(character)
                && !char.IsWhiteSpace(character)
                && character is not '.' and not '(' and not ')'
                && !MathOperators.Contains(character))
            {
                return false;
            }
        }

        return true;
    }
}
