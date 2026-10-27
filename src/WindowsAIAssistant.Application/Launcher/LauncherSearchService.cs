using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Application.Launcher;

/// <summary>
/// Turns one keystroke sequence into presentable rows. Local answers are tried before the
/// assistant, and a result that cannot be resolved locally still has a deliberate next step
/// rather than leaving the person at an empty list.
/// </summary>
public sealed class LauncherSearchService : ILauncherSearchService
{
    private readonly ILauncherIntentService _intents;
    private readonly ICalculatorService _calculator;
    private readonly ICommandPaletteService _commands;
    private readonly IUniversalSearchService _universal;
    private readonly LauncherSearchPolicy _policy;
    private readonly ILogger<LauncherSearchService> _logger;

    public LauncherSearchService(
        ILauncherIntentService intents,
        ICalculatorService calculator,
        ICommandPaletteService commands,
        IUniversalSearchService universal,
        LauncherSearchPolicy policy,
        ILogger<LauncherSearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(universal);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(logger);

        _intents = intents;
        _calculator = calculator;
        _commands = commands;
        _universal = universal;
        _policy = policy;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<LauncherResult> SearchAsync(
        LauncherRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var maximum = Math.Clamp(request.MaximumResults, 1, 50);
        var text = request.Text?.Trim() ?? string.Empty;
        var intent = _intents.Route(text);

        if (text.Length == 0)
        {
            var defaults = _commands
                .Search(string.Empty, maximum)
                .Select(LauncherItem.FromSearchResult)
                .ToArray();

            return LauncherResult.Success(intent, defaults);
        }

        if (intent.Type == LauncherIntentType.Calculator)
        {
            return Calculate(intent, maximum);
        }

        if (intent.Type == LauncherIntentType.Command)
        {
            var commandResults = _commands
                .Search(intent.Query, maximum)
                .Select(LauncherItem.FromSearchResult)
                .ToArray();

            if (commandResults.Length > 0)
            {
                return LauncherResult.Success(intent, commandResults);
            }
        }

        if (intent.Type == LauncherIntentType.Setting)
        {
            var settingResults = _commands
                .Search(intent.Query, maximum)
                .Where(result => result.Type == SearchResultType.Setting)
                .Select(LauncherItem.FromSearchResult)
                .ToArray();

            if (settingResults.Length > 0)
            {
                return LauncherResult.Success(intent, settingResults);
            }
        }

        try
        {
            var local = await _universal
                .SearchAsync(intent.Query, maximum, cancellationToken)
                .ConfigureAwait(false);

            return Compose(intent, local, maximum);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("A launcher search exceeded its overall budget.");
            return LauncherResult.Success(intent, [AskItem(intent.Query, 0.4)]);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A launcher search failed.");
            return LauncherResult.Failure(
                intent,
                ErrorCodes.LauncherSearchFailed,
                "Search could not finish. Try again or press Enter to ask the assistant.");
        }
    }

    private LauncherResult Calculate(LauncherIntent intent, int maximum)
    {
        var calculation = _calculator.Evaluate(intent.Query);
        if (calculation.IsFailure)
        {
            return LauncherResult.Failure(
                intent,
                calculation.ErrorCode ?? ErrorCodes.LauncherCalculationInvalid,
                calculation.ErrorMessage ?? "That expression cannot be calculated.");
        }

        var value = calculation.Value;
        var formatted = value.ToString("G15", CultureInfo.InvariantCulture);

        var item = new LauncherItem
        {
            Id = $"calculator:{intent.Query}",
            Title = formatted,
            Description = $"{intent.Query}  ·  Enter copies the result",
            Type = SearchResultType.Calculator,
            Group = "Calculator",
            Score = 1,
            Glyph = "\uE8EF",
            Action = LauncherAction.For(LauncherActionKind.CopyText, formatted),
        };

        return LauncherResult.Success(intent, [item]);
    }

    private static LauncherResult Compose(
        LauncherIntent intent,
        IReadOnlyList<SearchResult> results,
        int maximum)
    {
        var items = new List<LauncherItem>(maximum + 1);

        if (intent.Type == LauncherIntentType.AiQuestion)
        {
            items.Add(AskItem(intent.Query, 1));
        }
        else if (intent.Type == LauncherIntentType.WebSearch)
        {
            items.Add(WebItem(intent.Query));
        }

        foreach (var result in results
            .OrderByDescending(result => result.Score + PreferenceBoost(intent, result))
            .ThenBy(result => result.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            if (items.Count >= maximum)
            {
                break;
            }

            items.Add(LauncherItem.FromSearchResult(result));
        }

        if (items.Count == 0 && intent.Type is not LauncherIntentType.WebSearch)
        {
            items.Add(AskItem(intent.Query, 0.4));
        }

        if (intent.Type == LauncherIntentType.AiQuestion)
        {
            return LauncherResult.Success(intent, items) with
            {
                Message = "Enter sends this question to the assistant.",
            };
        }

        if (intent.Type == LauncherIntentType.WebSearch)
        {
            return LauncherResult.Success(intent, items) with
            {
                Message = "Enter searches the web.",
            };
        }

        return LauncherResult.Success(intent, items);
    }

    private static double PreferenceBoost(LauncherIntent intent, SearchResult result)
    {
        var preferred = intent.Type switch
        {
            LauncherIntentType.Application => SearchResultType.Application,
            LauncherIntentType.File => SearchResultType.File,
            LauncherIntentType.Knowledge => SearchResultType.Knowledge,
            _ => (SearchResultType?)null,
        };

        return preferred == result.Type ? 0.1 : 0;
    }

    private static LauncherItem AskItem(string query, double score) =>
        new()
        {
            Id = $"ai:{query}",
            Title = $"Ask the assistant: {query}",
            Description = "Send this to the conversation.",
            Type = SearchResultType.Ai,
            Group = "Assistant",
            Score = score,
            Glyph = "\uE8BD",
            Action = LauncherAction.For(LauncherActionKind.AskAssistant, query),
        };

    private static LauncherItem WebItem(string query) =>
        new()
        {
            Id = $"web:{query}",
            Title = $"Search the web for “{query}”",
            Description = "Open your default browser.",
            Type = SearchResultType.Web,
            Group = "Web",
            Score = 1,
            Glyph = "\uE774",
            Action = LauncherAction.For(
                LauncherActionKind.OpenUri,
                $"https://www.google.com/search?q={Uri.EscapeDataString(query)}"),
        };
}
