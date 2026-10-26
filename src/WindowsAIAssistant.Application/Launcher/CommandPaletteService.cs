using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Application.Launcher;

/// <summary>
/// The fixed set of commands and destinations the launcher offers.
/// </summary>
public sealed class CommandPaletteService : ICommandPaletteService
{
    private static readonly IReadOnlyList<CommandDefinition> Commands = BuildCommands();

    /// <inheritdoc />
    public IReadOnlyList<SearchResult> Search(string query, int maximumResults)
    {
        var trimmed = query?.Trim() ?? string.Empty;

        return Commands
            .Select(command => (Command: command, Score: Score(trimmed, command)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Command.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Max(1, maximumResults))
            .Select(candidate => new SearchResult
            {
                Id = $"command:{candidate.Command.Id}",
                Title = candidate.Command.Title,
                Description = candidate.Command.Description,
                Type = candidate.Command.Type,
                Group = candidate.Command.Type == SearchResultType.Setting ? "Settings" : "Commands",
                Score = candidate.Score,
                Glyph = candidate.Command.Glyph,
                Action = candidate.Command.Action,
            })
            .ToArray();
    }

    private static double Score(string query, CommandDefinition command)
    {
        if (query.Length == 0)
        {
            return 1;
        }

        if (command.Title.Equals(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return 1;
        }

        if (command.Title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return 0.9;
        }

        if (command.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || command.Aliases.Any(alias =>
                alias.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
        {
            return 0.7;
        }

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.All(word =>
            command.Title.Contains(word, StringComparison.CurrentCultureIgnoreCase))
            ? 0.5
            : 0;
    }

    private static IReadOnlyList<CommandDefinition> BuildCommands() =>
    [
        new("home", "Open Home", "The assistant landing page.", "Home", "\uE80F", SearchResultType.Command, ["start", "assistant"]),
        new("chat", "Open Chat", "Ask the assistant a question.", "Chat", "\uE8BD", SearchResultType.Command, ["ask", "conversation"]),
        new("files", "Search Files", "Find a file by name.", "Files", "\uE8A5", SearchResultType.Command, ["find", "file"]),
        new("document", "Open Document", "Read, summarize, or ask about a document.", "Document", "\uE8A5", SearchResultType.Command, ["summarize", "pdf", "word"]),
        new("knowledge", "Open Knowledge", "Search your indexed documents.", "Knowledge", "\uE8F1", SearchResultType.Command, ["documents", "index", "rag"]),
        new("agent", "Open Agent", "Watch a request planned and executed.", "Agent", "\uE8D4", SearchResultType.Command, ["workspace", "plan"]),
        new("automations", "Open Automations", "Manage saved automations.", "Automations", "\uE768", SearchResultType.Command, ["tasks"]),
        new("settings", "Open Assistant Settings", "Change how the assistant behaves.", "Settings", "\uE713", SearchResultType.Setting, ["preferences", "config"]),
        new("windows-settings", "Open Windows Settings", "Open the Windows Settings application.", "ms-settings:", "\uE713", SearchResultType.Setting, ["system", "windows"]),
    ];

    private sealed record CommandDefinition(
        string Id,
        string Title,
        string Description,
        string Route,
        string Glyph,
        SearchResultType Type,
        IReadOnlyList<string> Aliases)
    {
        public LauncherAction Action { get; } = Route.StartsWith("ms-", StringComparison.OrdinalIgnoreCase)
            ? LauncherAction.For(LauncherActionKind.OpenUri, Route)
            : LauncherAction.For(LauncherActionKind.Navigate, Route);
    }
}
