using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Automations;
using WindowsAIAssistant.Core.Models.Documents;
using WindowsAIAssistant.Infrastructure.Automations;
using WindowsAIAssistant.Infrastructure.Documents;

namespace WindowsAIAssistant.Application.Tests.Automations;

public sealed class AutomationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "automation-tests-" + Guid.NewGuid());
    private readonly Clock _clock = new();
    private readonly Documents _documents = new();
    private readonly FakePermissionService _permissions = new();

    public AutomationTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, recursive: true);
    private AutomationService Service() => new(Path.Combine(_folder, "schedules.json"), _documents, new DocumentTypeDetector(), _permissions, _clock);
    private Automation Reminder(AutomationRepeat repeat = AutomationRepeat.Once) => new()
    {
        Title = "Review", Details = "Review your goals", TimeZoneId = "UTC",
        StartLocal = _clock.Now.UtcDateTime.AddMinutes(1), Repeat = repeat,
    };

    [Fact]
    public async Task OverdueReminderSurvivesRestartAndRunsOnlyOnce()
    {
        var original = Service();
        await original.SaveAsync(Reminder());
        _clock.Now = _clock.Now.AddHours(2);
        var reopened = Service();
        var notifications = 0;
        reopened.Completed += (_, _) => notifications++;
        await reopened.RunDueAsync();
        await reopened.RunDueAsync();
        var item = Assert.Single(await Service().ListAsync());
        Assert.Equal(1, notifications);
        Assert.Equal("Review your goals", item.LastResult);
        Assert.False(item.Enabled);
        Assert.Null(item.NextRunUtc);
        Assert.Equal("Completed", item.Status);
    }

    [Fact]
    public async Task WeeklyScheduleCatchesUpOnceRatherThanReplayingMissedWeeks()
    {
        var service = Service();
        var reminder = Reminder(AutomationRepeat.Weekly);
        await service.SaveAsync(reminder);
        _clock.Now = _clock.Now.AddDays(24);
        await Task.WhenAll(service.RunDueAsync(), service.RunDueAsync());
        var item = Assert.Single(await service.ListAsync());
        Assert.True(item.NextRunUtc > _clock.Now);
        Assert.Equal(reminder.StartLocal.DayOfWeek, item.NextRunUtc!.Value.DayOfWeek);
        Assert.Equal(_clock.Now, item.LastRunUtc);
    }

    [Fact]
    public async Task PausedScheduleDoesNotRunAndCanBeResumedAndDeleted()
    {
        var service = Service();
        var reminder = Reminder(AutomationRepeat.Daily);
        await service.SaveAsync(reminder);
        await service.SetEnabledAsync(reminder.Id, false);
        _clock.Now = _clock.Now.AddDays(2);
        await service.RunDueAsync();
        Assert.Null(Assert.Single(await service.ListAsync()).LastRunUtc);
        await service.SetEnabledAsync(reminder.Id, true);
        Assert.True(Assert.Single(await service.ListAsync()).NextRunUtc > _clock.Now);
        await service.DeleteAsync(reminder.Id);
        Assert.Empty(await Service().ListAsync());
    }

    [Fact]
    public async Task FolderSummaryHonorsPermissionsAndDoesNotReadSubfolders()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, "notes.txt"), "Notes");
        Directory.CreateDirectory(Path.Combine(_folder, "nested"));
        await File.WriteAllTextAsync(Path.Combine(_folder, "nested", "private.txt"), "Private");
        var service = Service();
        var item = Reminder() with { Kind = AutomationKind.FolderSummary, Details = _folder };
        await service.SaveAsync(item);
        _permissions.Denied.Add(PermissionCapability.DocumentCloudProcessing);
        await service.RunAsync(item.Id);
        Assert.Equal(0, _documents.Calls);
        Assert.Equal("Failed", Assert.Single(await service.ListAsync()).Status);
        _permissions.Denied.Clear();
        await service.RunAsync(item.Id);
        Assert.Equal(1, _documents.Calls);
        Assert.Contains("notes.txt", Assert.Single(await service.ListAsync()).LastResult);
        Assert.DoesNotContain("private.txt", Assert.Single(await service.ListAsync()).LastResult);
    }

    [Fact]
    public async Task ConcurrentRunsCannotSummarizeTheSameJobTwice()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, "notes.txt"), "Notes");
        var service = Service();
        var item = Reminder() with { Kind = AutomationKind.FolderSummary, Details = _folder };
        await service.SaveAsync(item);
        _documents.Block = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = service.RunAsync(item.Id);
        await _documents.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.RunAsync(item.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(item.Id));
        _documents.Block.SetResult();
        await running;
        Assert.Equal(1, _documents.Calls);
    }

    [Fact]
    public async Task CorruptStorageIsNotSilentlyReplaced()
    {
        var path = Path.Combine(_folder, "schedules.json");
        await File.WriteAllTextAsync(path, "broken json");
        await Assert.ThrowsAsync<global::System.Text.Json.JsonException>(() => Service().SaveAsync(Reminder()));
        Assert.Equal("broken json", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task PastOneTimeScheduleIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().SaveAsync(Reminder() with { StartLocal = _clock.Now.UtcDateTime.AddHours(-1) }));
    }

    [Fact]
    public void WeeklyScheduleRetainsLocalTimeAcrossDaylightSaving()
    {
        var item = Reminder(AutomationRepeat.Weekly) with
        {
            TimeZoneId = "Eastern Standard Time", StartLocal = new DateTime(2026, 3, 6, 9, 0, 0),
        };
        var next = AutomationSchedule.Next(item, new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 3, 13, 13, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public async Task InterruptedRunIsVisibleAfterRestartAndDoesNotReplay()
    {
        var service = Service();
        var reminder = Reminder();
        await service.SaveAsync(reminder);
        var stored = Assert.Single(await service.ListAsync()) with
        {
            Status = "Running", Enabled = false, NextRunUtc = null,
        };
        await File.WriteAllTextAsync(Path.Combine(_folder, "schedules.json"), global::System.Text.Json.JsonSerializer.Serialize(new[] { stored }));
        var reopened = Service();
        _clock.Now = _clock.Now.AddDays(1);
        await reopened.RunDueAsync();
        Assert.Equal("Interrupted", Assert.Single(await reopened.ListAsync()).Status);
    }

    [Fact]
    public async Task FolderProcessingIsLimitedToTwentyFiles()
    {
        for (var i = 0; i < 25; i++) await File.WriteAllTextAsync(Path.Combine(_folder, $"notes{i:D2}.txt"), "Notes");
        var service = Service();
        var item = Reminder() with { Kind = AutomationKind.FolderSummary, Details = _folder };
        await service.SaveAsync(item);
        await service.RunAsync(item.Id);
        Assert.Equal(20, _documents.Calls);
        Assert.Contains("Limited to the first 20", Assert.Single(await service.ListAsync()).LastResult);
    }

    [Fact]
    public async Task RunNowPreservesNextOccurrenceAndFailedRunsAdvanceTheirSchedule()
    {
        var service = Service();
        var reminder = Reminder(AutomationRepeat.Daily);
        await service.SaveAsync(reminder);
        var next = Assert.Single(await service.ListAsync()).NextRunUtc;
        await service.RunAsync(reminder.Id);
        Assert.Equal(next, Assert.Single(await service.ListAsync()).NextRunUtc);
        var summary = Reminder(AutomationRepeat.Daily) with { Kind = AutomationKind.FolderSummary, Details = _folder };
        await service.SaveAsync(summary);
        _permissions.Denied.Add(PermissionCapability.FileSearch);
        _clock.Now = _clock.Now.AddHours(2);
        await service.RunDueAsync();
        var failed = (await service.ListAsync()).Single(x => x.Id == summary.Id);
        Assert.Equal("Failed", failed.Status);
        Assert.Contains("FileSearch", failed.LastResult);
        Assert.True(failed.NextRunUtc > _clock.Now);
    }

    [Fact]
    public void NonexistentDaylightSavingTimeMovesToFirstValidMinute()
    {
        var item = Reminder() with { TimeZoneId = "Eastern Standard Time", StartLocal = new DateTime(2026, 3, 8, 2, 30, 0) };
        var next = AutomationSchedule.Next(item, new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public async Task ReminderCanCompleteWhileFolderSummaryIsStillRunning()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, "notes.txt"), "Notes");
        var service = Service();
        var summary = Reminder() with { Kind = AutomationKind.FolderSummary, Details = _folder };
        var reminder = Reminder() with { StartLocal = _clock.Now.UtcDateTime.AddMinutes(3) };
        await service.SaveAsync(summary);
        await service.SaveAsync(reminder);
        _documents.Block = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _clock.Now = _clock.Now.AddMinutes(2);
        var firstTick = service.RunDueAsync();
        await _documents.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _clock.Now = _clock.Now.AddMinutes(2);
        try
        {
            await service.RunDueAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Completed", (await service.ListAsync()).Single(x => x.Id == reminder.Id).Status);
            Assert.False(firstTick.IsCompleted);
        }
        finally { _documents.Block.SetResult(); await firstTick; }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Documents : IDocumentAnalysisService
    {
        public int Calls { get; private set; }
        public TaskCompletionSource? Block { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DocumentAnalysisResult> SummarizeAsync(DocumentAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Entered.TrySetResult();
            if (Block is not null) await Block.Task.WaitAsync(cancellationToken);
            return DocumentAnalysisResult.Success(DocumentAnalysisOperation.Summarize, request.FileName, DocumentFileType.PlainText, "Summary of notes");
        }
        public Task<DocumentAnalysisResult> AskQuestionAsync(DocumentAnalysisRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<DocumentAnalysisUpdate> StreamAnswerAsync(DocumentAnalysisRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
