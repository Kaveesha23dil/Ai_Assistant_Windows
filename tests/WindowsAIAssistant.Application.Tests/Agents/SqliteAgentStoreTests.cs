using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Agents;
using WindowsAIAssistant.Infrastructure.Agents;

namespace WindowsAIAssistant.Application.Tests.Agents;

/// <summary>
/// The agent's own SQLite file: what it keeps, what it refuses to keep, and whether it is still
/// there after the process restarts.
/// <para>
/// These run against a real file rather than the session-only store. The properties under test Ã¢â‚¬â€
/// a uniqueness constraint replacing rather than duplicating, an upsert being atomic, an absent
/// column reading back as absent, a retention bound actually deleting Ã¢â‚¬â€ are SQLite's, and the
/// in-memory store has different answers to none of them. Testing the in-memory one would prove
/// that the memory store works.
/// </para>
/// </summary>
public sealed class SqliteAgentStoreTests
{
    [Fact]
    public async Task TheFileIsCreatedOnFirstUseRatherThanAtStartup()
    {
        using var harness = AgentStoreHarness.Create();
        var path = harness.DatabasePath;

        // Constructing the harness resolved the path and built the stores, but nothing has read
        // or written yet. This is what keeps a person who never uses the agent from accumulating a
        // file of activity they never generated.
        Assert.False(File.Exists(path));

        await harness.Memories.ListAsync(AgentMemoryType.UserPreference);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task AMemorySurvivesTheStoreBeingReplaced()
    {
        using var harness = AgentStoreHarness.Create();

        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "output", "markdown"));

        // A fresh store over the same file is what a restart looks like from here.
        var (reopened, _) = harness.Reopen();

        var memory = await reopened.GetAsync(AgentMemoryType.UserPreference, "output");

        Assert.NotNull(memory);
        Assert.Equal("markdown", memory!.Value);
    }

    [Fact]
    public async Task RestatingAPreferenceReplacesItRatherThanAddingASecondRow()
    {
        using var harness = AgentStoreHarness.Create();

        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "output", "pdf"));
        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "output", "markdown"));

        // Two rows would make the answer depend on which one a reader happened to find, and
        // "delete one of them" is not something somebody should have to know about their own
        // preferences.
        var all = await harness.Memories.ListAsync(AgentMemoryType.UserPreference);
        var memory = await harness.Memories.GetAsync(AgentMemoryType.UserPreference, "output");

        Assert.Single(all);
        Assert.Equal("markdown", memory!.Value);
    }

    [Fact]
    public async Task RestatingAPreferenceKeepsHowOftenItHadAlreadyBeenUsed()
    {
        using var harness = AgentStoreHarness.Create();

        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "output", "pdf") with
            {
                UseCount = 7,
            });

        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "output", "markdown"));

        // Resetting the count would make a long-standing preference look freshly stated every time
        // somebody mentions it again, which is how a stale one survives being corrected.
        var memory = await harness.Memories.GetAsync(AgentMemoryType.UserPreference, "output");

        Assert.Equal(7, memory!.UseCount);
    }

    [Fact]
    public async Task KeysAreMatchedWithoutRegardToCase()
    {
        using var harness = AgentStoreHarness.Create();

        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "Output Format", "pdf"));

        // A lookup that ignored case while the write did not would let two rows exist for one
        // preference, and "output format" is how a person would reasonably ask for the same one.
        var found = await harness.Memories.GetAsync(AgentMemoryType.UserPreference, "output format");

        Assert.NotNull(found);
        Assert.Single(await harness.Memories.ListAsync(AgentMemoryType.UserPreference));
    }

    [Fact]
    public async Task MemoryTypesAreKeptApart()
    {
        using var harness = AgentStoreHarness.Create();

        // A preference and a workflow can share a key without being the same thing, and merging
        // them would let a workflow overwrite what somebody said they prefer.
        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "format", "pdf"));
        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.WorkflowHistory, "format", "search then report"));

        Assert.Single(await harness.Memories.ListAsync(AgentMemoryType.UserPreference));
        Assert.Single(await harness.Memories.ListAsync(AgentMemoryType.WorkflowHistory));
        Assert.Equal("pdf",
            (await harness.Memories.GetAsync(AgentMemoryType.UserPreference, "format"))!.Value);
        Assert.Equal("search then report",
            (await harness.Memories.GetAsync(AgentMemoryType.WorkflowHistory, "format"))!.Value);
    }

    [Fact]
    public async Task DeletingOneMemoryLeavesTheOthersAlone()
    {
        using var harness = AgentStoreHarness.Create();

        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "format", "pdf"));
        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "language", "en-GB"));

        var deleted = await harness.Memories.DeleteAsync(AgentMemoryType.UserPreference, "FORMAT");

        Assert.True(deleted);
        Assert.Null(await harness.Memories.GetAsync(AgentMemoryType.UserPreference, "format"));
        Assert.NotNull(await harness.Memories.GetAsync(AgentMemoryType.UserPreference, "language"));
    }

    [Fact]
    public async Task DeletingSomethingThatIsNotThereReportsThatItWasNotThere()
    {
        using var harness = AgentStoreHarness.Create();

        // A button that says "forgotten" when nothing was forgotten is worse than one that says
        // nothing changed, because it teaches somebody the button is unreliable.
        Assert.False(await harness.Memories.DeleteAsync(AgentMemoryType.UserPreference, "absent"));
    }

    [Fact]
    public async Task ClearingOneMemoryTypeDoesNotClearTheOther()
    {
        using var harness = AgentStoreHarness.Create();

        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "format", "pdf"));
        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.WorkflowHistory, "reports", "weekly"));

        var removed = await harness.Memories.ClearAsync(AgentMemoryType.UserPreference);

        Assert.Equal(1, removed);
        Assert.Empty(await harness.Memories.ListAsync(AgentMemoryType.UserPreference));
        Assert.Single(await harness.Memories.ListAsync(AgentMemoryType.WorkflowHistory));
    }

    [Fact]
    public async Task ForgettingEverythingIsPossibleAndIsTheNarrowOperationItSaysItIs()
    {
        using var harness = AgentStoreHarness.Create();

        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "format", "pdf"));
        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.WorkflowHistory, "reports", "weekly"));
        await harness.Activity.RecordAsync(AgentStoreHarness.Run("wrote a report", DateTimeOffset.UtcNow));

        await harness.Memories.ClearAsync();

        // This is the promise the whole separate-file decision rests on: "forget" removes the
        // agent's own records and touches nothing else on the machine.
        Assert.Empty(await harness.Memories.ListAsync(AgentMemoryType.UserPreference));
        Assert.Empty(await harness.Memories.ListAsync(AgentMemoryType.WorkflowHistory));
    }

    [Fact]
    public async Task APreferenceThatCannotBeSavedIsReportedRatherThanAssumedToHaveWorked()
    {
        using var harness = AgentStoreHarness.CreateUnwritable();

        // Memories are the one thing whose failure a person is told about. A preference that was
        // not saved and not reported would be silently forgotten at the next restart, and shown as
        // remembered until then, which is worse than having never offered to remember it.
        var failure = await Assert.ThrowsAsync<AgentException>(async () =>
            await harness.Memories.SaveAsync(
                AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "output", "pdf")));

        Assert.Equal(ErrorCodes.AgentMemoryUnavailable, failure.ErrorCode);
    }

    [Fact]
    public async Task AFailedReadIsAlsoReportedRatherThanLookingLikeAnAbsence()
    {
        using var harness = AgentStoreHarness.CreateUnwritable();

        // "There is no such preference" and "the store could not be opened" must not look the same.
        // Returning null for the second would tell somebody their preference had been forgotten.
        var failure = await Assert.ThrowsAsync<AgentException>(async () =>
            await harness.Memories.GetAsync(AgentMemoryType.UserPreference, "output"));

        Assert.Equal(ErrorCodes.AgentMemoryUnavailable, failure.ErrorCode);
    }

    [Fact]
    public async Task AFailedRunIsNotRecordedRatherThanBeingReportedAsAFailedRun()
    {
        using var harness = AgentStoreHarness.CreateUnwritable();

        // The run has already happened by the time it is recorded, and its plan has already been
        // shown. Reporting that run as failed because its history line could not be written would be
        // a lie about what the agent did, so the failure is logged and the run simply goes unrecorded.
        await harness.Activity.RecordAsync(AgentStoreHarness.Run("a run", DateTimeOffset.UtcNow));

        // There is nothing to read back, which is the honest outcome: the store could not be
        // opened, so it never claimed to have accepted anything.
        await Assert.ThrowsAsync<AgentException>(async () => await harness.Activity.GetRecentAsync());
    }

    [Fact]
    public async Task ARunThatStartedIsRecordedEvenBeforeItFinishes()
    {
        using var harness = AgentStoreHarness.Create();
        var started = DateTimeOffset.UtcNow;

        await harness.Activity.RecordAsync(AgentStoreHarness.Run("wrote a report", started));

        var recent = await harness.Activity.GetRecentAsync();

        // A run held in memory until it completes leaves nothing behind if the application dies
        // during it, and a timeline with a hole in it cannot tell a crash from a run that never
        // happened.
        var entry = Assert.Single(recent);
        Assert.Equal(AgentActivityStatus.Running, entry.Status);
        Assert.True(entry.IsRunning);
    }

    [Fact]
    public async Task ARunIsOneRowWhicheverSideOfItTheUpdateComesFrom()
    {
        using var harness = AgentStoreHarness.Create();
        var started = DateTimeOffset.UtcNow;
        var run = AgentStoreHarness.Run("wrote a report", started);

        await harness.Activity.RecordAsync(run);
        await harness.Activity.RecordAsync(run.AsFinished(
            AgentActivityStatus.Completed,
            ["KnowledgeSearchTool", "ReportGenerationTool"],
            TimeSpan.FromMilliseconds(1200),
            2));

        var recent = await harness.Activity.GetRecentAsync();
        var entry = Assert.Single(recent);

        Assert.Equal(AgentActivityStatus.Completed, entry.Status);
        Assert.Equal(["KnowledgeSearchTool", "ReportGenerationTool"], entry.ToolsUsed);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), entry.Duration);
    }

    [Fact]
    public async Task ToolNamesComeBackInTheOrderTheyWentIn()
    {
        using var harness = AgentStoreHarness.Create();
        var run = AgentStoreHarness.Run("a plan", DateTimeOffset.UtcNow);

        await harness.Activity.RecordAsync(run);
        await harness.Activity.RecordAsync(run.AsFinished(
            AgentActivityStatus.Completed,
            ["FirstTool", "SecondTool", "ThirdTool"],
            TimeSpan.Zero,
            3));

        var entry = Assert.Single(await harness.Activity.GetRecentAsync());

        // The order a plan ran in is the order the timeline shows it in, and a store that sorted
        // or deduplicated the list would put a person's plan in an order they never chose.
        Assert.Equal(["FirstTool", "SecondTool", "ThirdTool"], entry.ToolsUsed);
    }

    [Fact]
    public async Task AConversationWithNoIdentifierStaysOutOfEveryConversationsTimeline()
    {
        using var harness = AgentStoreHarness.Create();
        var conversation = Guid.NewGuid();

        await harness.Activity.RecordAsync(
            AgentStoreHarness.Run("in a conversation", DateTimeOffset.UtcNow, conversation));
        await harness.Activity.RecordAsync(
            AgentStoreHarness.Run("from a demonstration", DateTimeOffset.UtcNow));

        // A workspace demonstration is not part of somebody's conversation, however recent it is.
        var mine = await harness.Activity.GetForConversationAsync(conversation);

        Assert.Equal("in a conversation", Assert.Single(mine).Goal);
    }

    [Fact]
    public async Task AConversationIsReadOldestFirstBecauseThatIsTheOrderItHappenedIn()
    {
        using var harness = AgentStoreHarness.Create();
        var conversation = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;

        await harness.Activity.RecordAsync(
            AgentStoreHarness.Run("first", start, conversation));
        await harness.Activity.RecordAsync(
            AgentStoreHarness.Run("second", start.AddMinutes(1), conversation));
        await harness.Activity.RecordAsync(
            AgentStoreHarness.Run("third", start.AddMinutes(2), conversation));

        var mine = await harness.Activity.GetForConversationAsync(conversation);

        Assert.Equal(["first", "second", "third"], mine.Select(entry => entry.Goal));
    }

    [Fact]
    public async Task TheTimelineIsBoundedAndTheOldestRunsAreTheOnesThatGo()
    {
        // Three kept, five written. This is the property that makes the file's size predictable
        // rather than a function of how long the machine has been on.
        using var harness = AgentStoreHarness.Create(retention: 3);
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (var index = 0; index < 5; index++)
        {
            await harness.Activity.RecordAsync(
                AgentStoreHarness.Run($"run {index}", start.AddMinutes(index)));
        }

        var recent = await harness.Activity.GetRecentAsync(50);

        Assert.Equal(3, recent.Count);
        Assert.Equal(["run 4", "run 3", "run 2"], recent.Select(entry => entry.Goal));
    }

    [Fact]
    public async Task AskingForMoreRunsThanAreKeptGivesWhatThereIs()
    {
        using var harness = AgentStoreHarness.Create(retention: 2);

        await harness.Activity.RecordAsync(AgentStoreHarness.Run("only run", DateTimeOffset.UtcNow));

        // The count a caller asks for is a wish, not a promise. Returning fewer rows rather than
        // inventing them is what a bounded store means.
        Assert.Single(await harness.Activity.GetRecentAsync(1_000));
    }

    [Fact]
    public async Task RunsOlderThanACutOffAreRemovedAndNewerOnesAreNot()
    {
        using var harness = AgentStoreHarness.Create();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await harness.Activity.RecordAsync(AgentStoreHarness.Run("ancient", start));
        await harness.Activity.RecordAsync(AgentStoreHarness.Run("recent", start.AddDays(30)));

        var removed = await harness.Activity.PruneAsync(start.AddDays(1));

        Assert.Equal(1, removed);
        Assert.Equal("recent", Assert.Single(await harness.Activity.GetRecentAsync()).Goal);
    }

    [Fact]
    public async Task ClearingTheTimelineRemovesTheRunsAndOnlyTheRuns()
    {
        using var harness = AgentStoreHarness.Create();

        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "format", "pdf"));
        await harness.Activity.RecordAsync(AgentStoreHarness.Run("a run", DateTimeOffset.UtcNow));

        await harness.Activity.ClearAsync();

        Assert.Empty(await harness.Activity.GetRecentAsync());
        Assert.Single(await harness.Memories.ListAsync(AgentMemoryType.UserPreference));
    }

    [Fact]
    public async Task TwoStoresWritingTheSameKeyAtOnceLeaveOneRowWithOneOfTheValues()
    {
        using var harness = AgentStoreHarness.Create();

        // The uniqueness constraint, not a read-then-write, is what makes this hold. Two callers
        // that both checked and both wrote would otherwise leave two rows from which the next
        // reader picks whichever it finds first.
        var writes = new[]
        {
            Save(harness, "pdf"),
            Save(harness, "markdown"),
            Save(harness, "docx"),
            Save(harness, "pdf"),
        };

        await Task.WhenAll(writes);

        var all = await harness.Memories.ListAsync(AgentMemoryType.UserPreference);
        var memory = await harness.Memories.GetAsync(AgentMemoryType.UserPreference, "output");

        Assert.Single(all);
        Assert.Contains(memory!.Value, new[] { "pdf", "markdown", "docx" });
    }

    [Theory]
    [InlineData(@"..\..\evil.db")]
    [InlineData("../evil.db")]
    [InlineData("sub/evil.db")]
    [InlineData("C:evil.db")]
    public void AFileNameThatCouldEscapeItsOwnDirectoryIsRefusedRatherThanSanitized(string fileName)
    {
        var options = Options.Create(new AgentOptions { DatabaseFileName = fileName });

        // Making the name safe would mean quietly writing somewhere other than where the
        // configuration said, which is worse than refusing to start.
        var failure = Assert.Throws<AgentException>(
            () => new AgentDatabase(options, NullLogger<AgentDatabase>.Instance));

        Assert.Equal(ErrorCodes.AgentMemoryUnavailable, failure.ErrorCode);
    }

    [Fact]
    public void AnEmptyFileNameGetsTheDefaultRatherThanAFailure()
    {
        var options = Options.Create(new AgentOptions { DatabaseFileName = "  " });

        // An unset name is the normal case for a fresh install, not a misconfiguration.
        var database = new AgentDatabase(options, NullLogger<AgentDatabase>.Instance);

        database.Dispose();

        Assert.EndsWith("agent.db", database.DatabasePath);
    }

    [Fact]
    public async Task OpeningTheSameFileFromTwoDatabasesWorks()
    {
        using var harness = AgentStoreHarness.Create();

        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "format", "pdf"));

        // A second handle to the same file is what a second copy of the application would be. The
        // schema creation runs on both, and because every statement is IF NOT EXISTS the second
        // one is a no-op rather than an error.
        var (second, _) = harness.Reopen();

        Assert.NotNull(await second.GetAsync(AgentMemoryType.UserPreference, "format"));
    }

    [Fact]
    public async Task TheSchemaIsCreatedOnlyOnceHoweverManyTimesItIsAsked()
    {
        using var harness = AgentStoreHarness.Create();

        await harness.Database.EnsureInitializedAsync();
        await harness.Database.EnsureInitializedAsync();

        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "format", "pdf"));

        Assert.Single(await harness.Memories.ListAsync(AgentMemoryType.UserPreference));
    }

    [Fact]
    public async Task AStoredValueIsExactlyTheCharactersThatWereGiven()
    {
        using var harness = AgentStoreHarness.Create();

        // Commas and colons are the storage layer's own separators for a list, and a value that
        // happens to contain one has to come back as it went in rather than as two entries.
        var awkward = "always, never, and \"sometimes\": really";

        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "greeting", awkward));

        var memory = await harness.Memories.GetAsync(AgentMemoryType.UserPreference, "greeting");

        Assert.Equal(awkward, memory!.Value);
    }

    [Fact]
    public async Task ATimestampIsStoredAndReadBackInTheSameInstant()
    {
        using var harness = AgentStoreHarness.Create();

        // Offsets are normalized on the way in. Comparing text in UTC rather than in the writer's
        // local zone is what keeps the timeline in order across a daylight-saving change.
        var at = new DateTimeOffset(2026, 6, 1, 14, 30, 0, TimeSpan.FromHours(5.5));

        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "when", "yesterday", at));

        var memory = await harness.Memories.GetAsync(AgentMemoryType.UserPreference, "when");

        Assert.Equal(at.UtcDateTime, memory!.CreatedAt.UtcDateTime);
    }

    [Fact]
    public void AValueThatLooksLikeACredentialIsRefusedByTheRulesAboveTheStore()
    {
        // The store would happily hold this: it is a store, and its contract is to keep what it
        // is handed. What stops it is this rule, and the test says the two layers have not been
        // confused with one another.
        Assert.False(AgentMemoryRules.IsAllowed("my password is hunter2", out var refusal));
        Assert.False(string.IsNullOrWhiteSpace(refusal));
    }

    private static async Task Save(AgentStoreHarness harness, string value) =>
        await harness.Memories.SaveAsync(
            AgentStoreHarness.Memory(AgentMemoryType.UserPreference, "output", value));
}