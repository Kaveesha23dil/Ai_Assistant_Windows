using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models;

public enum FileSearchDateField { Modified, Created }

public sealed record FileSearchRequest(string Query)
{
    public KnownFolderKind? Location { get; init; }
    public string? FolderPath { get; init; }
    public string? Extension { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? Before { get; init; }
    public FileSearchDateField? DateField { get; init; }
    public long? MinimumSize { get; init; }
    public long? MaximumSize { get; init; }
    public bool IncludeSubfolders { get; init; } = true;
    public int MaximumResults { get; init; } = 200;
}

public sealed record FileSearchResponse(IReadOnlyCollection<FileSearchResult> Files, string Summary);
public sealed record FilePreview(string Text, string Notice);
