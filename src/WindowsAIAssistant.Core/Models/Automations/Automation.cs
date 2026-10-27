namespace WindowsAIAssistant.Core.Models.Automations;

public enum AutomationKind { Reminder, FolderSummary }
public enum AutomationRepeat { Once, Daily, Weekly }

public sealed record Automation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = string.Empty;
    public AutomationKind Kind { get; init; }
    public string Details { get; init; } = string.Empty;
    public AutomationRepeat Repeat { get; init; }
    public DateTime StartLocal { get; init; }
    public string TimeZoneId { get; init; } = TimeZoneInfo.Local.Id;
    public bool Enabled { get; init; } = true;
    public DateTimeOffset? NextRunUtc { get; init; }
    public DateTimeOffset? LastRunUtc { get; init; }
    public string Status { get; init; } = "Scheduled";
    public string LastResult { get; init; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    public string NextRunDisplay => Enabled && NextRunUtc is { } next
        ? TimeZoneInfo.ConvertTime(next, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)).ToString("f") + " (" + TimeZoneId + ")"
        : "No scheduled run";
    [System.Text.Json.Serialization.JsonIgnore]
    public string LastRunDisplay => LastRunUtc?.ToLocalTime().ToString("f") ?? "Never";
}

/// <summary>Wall-clock schedules retain the selected time across daylight-saving changes.</summary>
public static class AutomationSchedule
{
    public static DateTimeOffset? Next(Automation item, DateTimeOffset after)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(item.TimeZoneId);
        var candidate = DateTime.SpecifyKind(item.StartLocal, DateTimeKind.Unspecified);
        if (item.Repeat != AutomationRepeat.Once)
        {
            var localAfter = TimeZoneInfo.ConvertTime(after, zone).DateTime;
            var days = item.Repeat == AutomationRepeat.Daily ? 1 : 7;
            var steps = Math.Max(0, (int)((localAfter.Date - candidate.Date).TotalDays / days));
            candidate = candidate.AddDays(steps * days);
        }

        while (true)
        {
            // Move a nonexistent spring-forward time to the first valid minute.
            var valid = candidate;
            while (zone.IsInvalidTime(valid)) valid = valid.AddMinutes(1);
            var utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(valid, zone));
            if (utc > after) return utc;
            if (item.Repeat == AutomationRepeat.Once) return null;
            candidate = candidate.AddDays(item.Repeat == AutomationRepeat.Daily ? 1 : 7);
        }
    }
}
