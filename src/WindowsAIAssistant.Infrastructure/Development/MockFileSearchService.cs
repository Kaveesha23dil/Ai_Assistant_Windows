using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Infrastructure.Development;

public sealed class MockFileSearchService : IFileSearchService
{
    private static readonly IReadOnlyCollection<FileSearchResult> SampleResults =
    [
        new FileSearchResult(
            "ResearchPaper.pdf",
            @"C:\Development\ResearchPaper.pdf",
            ".pdf",
            120_000,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            0.95),
        new FileSearchResult(
            "ProjectNotes.docx",
            @"C:\Development\ProjectNotes.docx",
            ".docx",
            48_000,
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            0.90),
        new FileSearchResult(
            "Architecture.md",
            @"C:\Development\Architecture.md",
            ".md",
            16_000,
            new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
            0.85)
    ];

    public Task<IReadOnlyCollection<FileSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyCollection<FileSearchResult>>(
                Array.Empty<FileSearchResult>());
        }

        return Task.FromResult(SampleResults);
    }
}
