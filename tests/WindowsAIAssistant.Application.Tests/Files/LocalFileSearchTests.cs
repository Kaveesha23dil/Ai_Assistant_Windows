using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Infrastructure.Files;
using WindowsAIAssistant.Infrastructure.Windows;

namespace WindowsAIAssistant.Application.Tests.Files;

public sealed class LocalFileSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "local-search-" + Guid.NewGuid());
    private readonly FakePermissionService _permissions = new();
    private readonly FixedClock _clock = new();
    public LocalFileSearchTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);
    private LocalFileSearchService Service(int limit = 50_000) => new(new Folders(_root), _permissions, _clock, limit);
    private async Task<string> FileAsync(string name, string text = "sample contents")
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text);
        return path;
    }

    [Fact]
    public async Task SearchesRealNamesRecursivelyWithoutReadingContents()
    {
        await FileAsync("nested/INVOICE.PDF");
        await FileAsync("unrelated.txt", "invoice");
        var response = await Service().SearchAsync(new FileSearchRequest("invoice") { FolderPath = _root });
        Assert.Equal("INVOICE.PDF", Assert.Single(response.Files).Name);
        Assert.Contains(_root, response.Summary);
    }

    [Fact]
    public async Task ExplicitExtensionDateSizeAndRecursionFiltersAreApplied()
    {
        var path = await FileAsync("invoice.pdf", new string('x', 500));
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc));
        await FileAsync("invoice.txt", new string('x', 500));
        await FileAsync("nested/invoice.pdf", new string('x', 500));
        var request = new FileSearchRequest("invoice")
        {
            FolderPath = _root, Extension = "PDF", IncludeSubfolders = false,
            From = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero),
            Before = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero),
            MinimumSize = 400, MaximumSize = 600,
        };
        Assert.Equal(path, Assert.Single((await Service().SearchAsync(request)).Files).FullPath);
        Assert.Empty((await Service().SearchAsync(request with { MinimumSize = 550 })).Files);
    }

    [Fact]
    public void NaturalLanguageExampleUsesDownloadsCreationDateAndPreviousCalendarWeek()
    {
        var parsed = FileSearchQueryParser.Parse(new FileSearchRequest("Find the invoice PDF I downloaded last week."), _clock.Now, TimeZoneInfo.Utc);
        Assert.Equal(KnownFolderKind.Downloads, parsed.Filters.Location);
        Assert.Equal(FileSearchDateField.Created, parsed.Filters.DateField);
        Assert.Equal(".pdf", parsed.Filters.Extension);
        Assert.Equal(new[] { "invoice" }, parsed.Terms);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), parsed.Filters.From);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), parsed.Filters.Before);
    }

    [Fact]
    public async Task DownloadedDateUsesCreationRatherThanModifiedTime()
    {
        var path = await FileAsync("invoice.pdf");
        File.SetCreationTimeUtc(path, new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc));
        Assert.Single(await Service().SearchAsync("invoice PDF I downloaded last week"));
        Assert.Empty(await Service().SearchAsync("invoice PDF modified last week"));
    }

    [Fact]
    public void ExplicitFiltersOverrideNaturalLanguageAndQuotedNamesStayLiteral()
    {
        var request = new FileSearchRequest("invoice pdf downloaded last week")
        {
            Location = KnownFolderKind.Documents, Extension = ".txt", DateField = FileSearchDateField.Modified,
            From = _clock.Now.AddDays(-1), Before = _clock.Now,
        };
        var parsed = FileSearchQueryParser.Parse(request, _clock.Now, TimeZoneInfo.Utc);
        Assert.Equal(KnownFolderKind.Documents, parsed.Filters.Location);
        Assert.Equal(".txt", parsed.Filters.Extension);
        Assert.Equal(request.From, parsed.Filters.From);
        Assert.Equal(FileSearchDateField.Modified, parsed.Filters.DateField);
        var literal = FileSearchQueryParser.Parse(new FileSearchRequest("\"my last week.pdf\""), _clock.Now, TimeZoneInfo.Utc);
        Assert.Equal(new[] { "my last week.pdf" }, literal.Terms);
        Assert.Null(literal.Filters.From);
    }

    [Fact]
    public async Task DuplicateRootsDoNotDuplicateResultsAndWildcardExtensionWorks()
    {
        await FileAsync("report.pdf");
        await FileAsync("report.txt");
        Assert.Single(await Service().SearchAsync("*.pdf"));
        Assert.Equal(2, (await Service().SearchAsync("*")).Count);
    }

    [Fact]
    public async Task ResultAndScanLimitsAreReported()
    {
        await FileAsync("first.txt");
        await FileAsync("second.txt");
        var response = await Service().SearchAsync(new FileSearchRequest("*") { FolderPath = _root, MaximumResults = 1 });
        Assert.Single(response.Files);
        Assert.Contains("best 1", response.Summary);
        var bounded = await Service(1).SearchAsync(new FileSearchRequest("*") { FolderPath = _root });
        Assert.Contains("partial", bounded.Summary);
    }

    [Fact]
    public async Task MissingFoldersInvalidFiltersPermissionDenialAndCancellationAreExplicit()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().SearchAsync(new FileSearchRequest("*") { FolderPath = Path.Combine(_root, "missing") }));
        await Assert.ThrowsAsync<ArgumentException>(() => Service().SearchAsync(new FileSearchRequest("*") { MinimumSize = 2, MaximumSize = 1 }));
        _permissions.Denied.Add(PermissionCapability.FileSearch);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service().SearchAsync("report"));
        _permissions.Denied.Clear();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().SearchAsync("report", new CancellationToken(true)));
    }

    [Fact]
    public async Task DateRangeEndIsExclusiveAndNoSubfoldersMeansNoNestedResults()
    {
        var path = await FileAsync("report.txt");
        var before = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(path, before.UtcDateTime);
        await FileAsync("nested/other.txt");
        var response = await Service().SearchAsync(new FileSearchRequest("*") { FolderPath = _root, IncludeSubfolders = false, Before = before });
        Assert.Empty(response.Files);
    }

    [Fact]
    public async Task FileOpeningHonorsPermissionsAndReportsMissingFilesWithoutLaunchingAnything()
    {
        var service = new UriLauncherService(NullLogger<UriLauncherService>.Instance, _permissions);
        _permissions.Denied.Add(PermissionCapability.ApplicationLaunch);
        var denied = await service.OpenAsync(new Uri(Path.Combine(_root, "file with spaces.txt")));
        Assert.True(denied.IsFailure);
        Assert.Contains("ApplicationLaunch", denied.ErrorMessage);
        _permissions.Denied.Clear();
        var missing = await service.OpenAsync(new Uri(Path.Combine(_root, "missing.txt")));
        Assert.Contains("moved or been deleted", missing.ErrorMessage);
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now => new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Folders(string root) : IKnownFolderService
    {
        public Task<Result<string>> GetPathAsync(KnownFolderKind folder, CancellationToken cancellationToken = default) => Task.FromResult(Result<string>.Success(root));
    }
}
