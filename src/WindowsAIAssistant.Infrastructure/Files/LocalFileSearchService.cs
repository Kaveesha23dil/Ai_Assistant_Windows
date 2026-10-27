using System.Diagnostics;
using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Infrastructure.Files;

public sealed class LocalFileSearchService(IKnownFolderService folders, IPermissionService permissions,
    TimeProvider? clock = null, int maximumEntries = 50_000, TimeSpan? timeLimit = null) : IFileSearchService
{
    public async Task<IReadOnlyCollection<FileSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        (await SearchAsync(new FileSearchRequest(query), cancellationToken)).Files;

    public async Task<FileSearchResponse> SearchAsync(FileSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsurePermission();
        cancellationToken.ThrowIfCancellationRequested();
        var parsed = FileSearchQueryParser.Parse(request, (clock ?? TimeProvider.System).GetUtcNow(), TimeZoneInfo.Local);
        var filter = parsed.Filters;
        if (filter.MaximumResults is < 1 or > 1000 || filter.MinimumSize < 0 || filter.MaximumSize < 0 ||
            filter.MinimumSize > filter.MaximumSize || filter.From >= filter.Before)
            throw new ArgumentException("Check the result limit, size range, and date range.");
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter.FolderPath))
        {
            if (!Path.IsPathFullyQualified(filter.FolderPath) || !Directory.Exists(filter.FolderPath))
                throw new ArgumentException("Enter the full path to an existing folder.");
            roots.Add(Path.GetFullPath(filter.FolderPath));
        }
        else
        {
            var locations = filter.Location is { } location ? new[] { location } : Enum.GetValues<KnownFolderKind>();
            foreach (var kind in locations)
            {
                var resolved = await folders.GetPathAsync(kind, cancellationToken).ConfigureAwait(false);
                if (resolved.IsSuccess && Directory.Exists(resolved.Value)) roots.Add(resolved.Value!);
            }
        }
        if (roots.Count == 0) throw new IOException("No search folders are available. Choose an existing folder.");
        return await Task.Run(() => Scan(parsed, roots, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private FileSearchResponse Scan(FileSearchQueryParser.Parsed parsed, List<string> roots, CancellationToken token)
    {
        var filter = parsed.Filters;
        var extension = filter.Extension?.Trim().TrimStart('.');
        var matches = new List<FileSearchResult>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(roots);
        var stopwatch = Stopwatch.StartNew();
        var entries = 0;
        var skipped = 0;
        var limited = false;
        while (queue.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            EnsurePermission();
            var directory = queue.Dequeue();
            if (!visited.Add(Path.GetFullPath(directory))) continue;
            try
            {
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", new EnumerationOptions
                { IgnoreInaccessible = false, AttributesToSkip = 0, RecurseSubdirectories = false }))
                {
                    token.ThrowIfCancellationRequested();
                    EnsurePermission();
                    if (++entries > maximumEntries || stopwatch.Elapsed > (timeLimit ?? TimeSpan.FromSeconds(15)))
                    { limited = true; break; }
                    try
                    {
                        var attributes = entry.Attributes;
                        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) { skipped++; continue; }
                        if (entry is DirectoryInfo)
                        {
                            if (filter.IncludeSubfolders && !entry.Name.StartsWith('.')) queue.Enqueue(entry.FullName);
                            continue;
                        }
                        if (entry is not FileInfo file) continue;
                        if (!string.IsNullOrEmpty(extension) && !file.Extension.TrimStart('.').Equals(extension, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!parsed.Terms.All(term => file.Name.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
                        var date = new DateTimeOffset(filter.DateField == FileSearchDateField.Created ? file.CreationTimeUtc : file.LastWriteTimeUtc);
                        if (filter.From is { } from && date < from || filter.Before is { } before && date >= before) continue;
                        var size = file.Length;
                        if (filter.MinimumSize is { } min && size < min || filter.MaximumSize is { } max && size > max) continue;
                        var score = parsed.Terms.Count == 0 ? .5 :
                            file.Name.Equals(string.Join(' ', parsed.Terms), StringComparison.OrdinalIgnoreCase) ? 1 :
                            file.Name.StartsWith(parsed.Terms[0], StringComparison.OrdinalIgnoreCase) ? .9 : .7;
                        matches.Add(new FileSearchResult(file.Name, file.FullName, file.Extension, size,
                            new DateTimeOffset(file.LastWriteTimeUtc), score, new DateTimeOffset(file.CreationTimeUtc)));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { skipped++; }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { skipped++; }
            if (limited) break;
        }
        EnsurePermission();
        token.ThrowIfCancellationRequested();
        var results = matches.OrderByDescending(x => x.RelevanceScore).ThenByDescending(x => x.LastModified)
            .ThenBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase).Take(filter.MaximumResults).ToArray();
        var summary = $"{results.Length} file(s). Searched: {string.Join("; ", roots)}.";
        if (!string.IsNullOrEmpty(extension)) summary += $" Type: .{extension}.";
        if (filter.From is not null || filter.Before is not null)
            summary += $" {filter.DateField ?? FileSearchDateField.Modified}: {filter.From?.ToLocalTime():d} to {filter.Before?.ToLocalTime():d} (end excluded).";
        if (limited) summary += " Search limit reached; results are partial. Narrow the folder or filters.";
        if (matches.Count > results.Length) summary += $" Showing the best {results.Length} matches. Narrow the search for more.";
        if (skipped > 0) summary += $" Skipped {skipped} inaccessible, system, or linked entries.";
        return new FileSearchResponse(results, summary);
    }

    private void EnsurePermission()
    {
        if (!permissions.IsGranted(PermissionCapability.FileSearch))
            throw new UnauthorizedAccessException(permissions.GetDeniedMessage(PermissionCapability.FileSearch));
    }
}
