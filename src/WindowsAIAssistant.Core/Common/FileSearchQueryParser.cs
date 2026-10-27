using System.Text.RegularExpressions;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Common;

/// <summary>Small deterministic query vocabulary; explicit controls take precedence.</summary>
public static class FileSearchQueryParser
{
    public sealed record Parsed(FileSearchRequest Filters, IReadOnlyList<string> Terms);

    public static Parsed Parse(FileSearchRequest request, DateTimeOffset now, TimeZoneInfo zone)
    {
        var text = request.Query ?? string.Empty;
        if (text.Length > 1000) throw new ArgumentException("Keep the search to 1000 characters or fewer.");
        if (text.StartsWith('"') && text.EndsWith('"') && text.Length > 1)
            return new Parsed(request, [text[1..^1]]);
        text = text.Replace("*.", ".", StringComparison.Ordinal);
        var filters = request;
        var localDate = TimeZoneInfo.ConvertTime(now, zone).Date;
        DateTimeOffset Utc(DateTime date) => new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), zone));
        if (Regex.IsMatch(text, @"\b(downloaded|downloads)\b", RegexOptions.IgnoreCase))
            filters = filters with { Location = request.Location ?? KnownFolderKind.Downloads, DateField = request.DateField ?? FileSearchDateField.Created };
        else if (Regex.IsMatch(text, @"\bdocuments\b", RegexOptions.IgnoreCase))
            filters = filters with { Location = request.Location ?? KnownFolderKind.Documents };
        else if (Regex.IsMatch(text, @"\bdesktop\b", RegexOptions.IgnoreCase))
            filters = filters with { Location = request.Location ?? KnownFolderKind.Desktop };
        if (request.DateField is null && Regex.IsMatch(text, @"\bcreated\b", RegexOptions.IgnoreCase))
            filters = filters with { DateField = FileSearchDateField.Created };

        DateTime? start = null, end = null;
        if (Regex.IsMatch(text, @"\blast week\b", RegexOptions.IgnoreCase))
        {
            end = localDate.AddDays(-((int)localDate.DayOfWeek + 6) % 7);
            start = end.Value.AddDays(-7);
        }
        else if (Regex.IsMatch(text, @"\b(last 7 days|past 7 days)\b", RegexOptions.IgnoreCase)) { start = localDate.AddDays(-6); end = localDate.AddDays(1); }
        else if (Regex.IsMatch(text, @"\byesterday\b", RegexOptions.IgnoreCase)) { start = localDate.AddDays(-1); end = localDate; }
        else if (Regex.IsMatch(text, @"\btoday\b", RegexOptions.IgnoreCase)) { start = localDate; end = localDate.AddDays(1); }
        else if (Regex.IsMatch(text, @"\bthis month\b", RegexOptions.IgnoreCase)) { start = new DateTime(localDate.Year, localDate.Month, 1); end = start.Value.AddMonths(1); }
        if (request.From is null && request.Before is null && start is not null)
            filters = filters with { From = Utc(start.Value), Before = Utc(end!.Value) };
        text = Regex.Replace(text, @"\b(last week|last 7 days|past 7 days|yesterday|today|this month)\b", " ", RegexOptions.IgnoreCase);
        var type = Regex.Match(text, @"(?<!\w)\.?(pdf|docx|xlsx|pptx|txt|md|csv|json|png|jpg|jpeg|mp4|zip)(?!\w)", RegexOptions.IgnoreCase);
        if (type.Success)
        {
            filters = filters with { Extension = request.Extension ?? "." + type.Groups[1].Value.ToLowerInvariant() };
            text = text.Remove(type.Index, type.Length);
        }
        text = Regex.Replace(text, @"\b(find|search|for|the|a|an|my|me|please|file|files|that|i|have|in|from|on|was|were|downloaded|downloads|documents|desktop|modified|created)\b", " ", RegexOptions.IgnoreCase);
        var terms = Regex.Matches(text, "\"([^\"]+)\"|([^\\s]+)")
            .Select(match => (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim('?', ',', ';', '.'))
            .Where(term => term.Length > 0 && term != "*").ToArray();
        return new Parsed(filters, terms);
    }
}
