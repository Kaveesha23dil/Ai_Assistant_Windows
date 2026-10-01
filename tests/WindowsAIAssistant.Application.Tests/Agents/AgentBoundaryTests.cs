using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Agents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;
using WindowsAIAssistant.Infrastructure.Agents;

namespace WindowsAIAssistant.Application.Tests.Agents;

/// <summary>
/// Covers the two guarantees that protect the machine and the person's data: that a tool cannot
/// change something without a switch, and that what a run leaves behind describes the run rather
/// than the material it read.
/// </summary>
public sealed class AgentBoundaryTests
{
    [Fact]
    public void TheRegistryRefusesAToolThatWritesWithoutNamingASwitch()
    {
        // Without this, a tool that declares an action but no RequiredPermission is "available"
        // by definition — there is nothing to consent to — and it is the one tool on the machine
        // able to write a file with every switch off.
        var exception = Assert.Throws<InvalidOperationException>(() => new ToolRegistry(
            [new UndeclaredWriteTool()],
            new Fakes.FakePermissionService(),
            NullLogger<ToolRegistry>.Instance));

        Assert.Contains("RequiredPermission", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRegistryAcceptsAWriteToolThatNamesItsSwitch()
    {
        var registry = new ToolRegistry(
            [new DeclaredWriteTool()],
            new Fakes.FakePermissionService(),
            NullLogger<ToolRegistry>.Instance);

        Assert.Equal(ToolAvailability.Available, registry.Check("DeclaredWriteTool"));
    }

    [Fact]
    public void TheRegistryRefusesTwoToolsAnsweringToOneName()
    {
        // Resolved by luck rather than by list, this would make a plan non-deterministic.
        Assert.Throws<InvalidOperationException>(() => new ToolRegistry(
            [new DeclaredWriteTool(), new DeclaredWriteTool()],
            new Fakes.FakePermissionService(),
            NullLogger<ToolRegistry>.Instance));
    }

    [Fact]
    public void ARefusedRunIsRecordedWithoutThePersonsWords()
    {
        // The request is exactly the text that may be somebody's document contents or a dictated
        // password, so a truncated copy of it is still a copy.
        const string request = "The client said the passphrase is hunter2 and the invoice is due";

        var activity = AgentActivity.Failed(
            AgentRequestContext.Spoken(request),
            "I could not work out what to do with that.",
            ErrorCodes.AgentIntentUnrecognized);

        Assert.DoesNotContain("hunter2", activity.Goal, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passphrase", activity.Goal, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hunter2", activity.Note ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // The refusal is still legible, because the note is written by this code.
        Assert.Contains("not understood", activity.Goal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("could not work out", activity.Note!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnActivityCarriesTheToolsAndCountsRatherThanWhatTheyRead()
    {
        var result = AgentExecutionResult.Completed(
            Guid.NewGuid(),
            AgentPlan.Single("Answer", "SearchTool", "Search", "what does it say"),
            [AgentStep
                .Create("SearchTool", "Search")
                .AsSucceeded(ToolResult.Success("SearchTool", "the answer is 42", ["a.txt", "b.txt"]))],
            "the answer is 42",
            TimeSpan.FromMilliseconds(12),
            ["a.txt", "b.txt"],
            ToolResult.Data1("found", 2));

        var activity = AgentActivity.FromResult(
            AgentRequestContext.Typed("what does it say"),
            result);

        Assert.Equal(["SearchTool"], activity.ToolsUsed);
        Assert.Equal(2, activity.SourceCount);

        // The source names are deliberately not carried across: a count says the answer was
        // grounded, and keeping the two names would keep a directory listing.
        Assert.DoesNotContain("a.txt", activity.Note ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTimelineNoteIsBuiltFromMetadataAlone()
    {
        var note = AgentActivity.BuildNote(["SearchTool", "AnswerTool"], 3, 2);

        Assert.Contains("SearchTool", note, StringComparison.Ordinal);
        Assert.Contains("3 source(s)", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordedRunIsTaggedWithTheConversationItBelongsTo()
    {
        // A run has to be findable again by the conversation it happened in, or the workspace
        // cannot show someone what the agent did while they were talking to it.
        var conversationId = Guid.NewGuid();
        var context = AgentRequestContext.Typed("what does it say") with { ConversationId = conversationId };

        var completed = AgentActivity.FromResult(
            context,
            AgentExecutionResult.Completed(
                Guid.NewGuid(),
                AgentPlan.Single("Answer", "SearchTool", "Search", "what does it say"),
                [AgentStep.Create("SearchTool", "Search").AsSucceeded(ToolResult.Success("SearchTool", "42"))],
                "42",
                TimeSpan.FromMilliseconds(12),
                [],
                null));

        var refused = AgentActivity.Failed(
            context,
            "I could not work out what to do with that.",
            ErrorCodes.AgentIntentUnrecognized);

        var stopped = AgentActivity.Failed(
            context,
            "Stopped before it went any further.");

        Assert.Equal(conversationId, completed.ConversationId);
        Assert.Equal(conversationId, refused.ConversationId);
        Assert.Equal(conversationId, stopped.ConversationId);
    }

    [Fact]
    public void ARunOutsideAnyConversationIsRecordedWithoutOne()
    {
        // Chat and voice supply a conversation; a demonstration started from the workspace does
        // not. That has to be a legitimate state rather than a null in a non-nullable column.
        var activity = AgentActivity.Failed(
            AgentRequestContext.Demo(new DemoScenario("d", "Demo", "prompt", "why", ["SearchTool"], [])),
            "Refused.",
            ErrorCodes.AgentIntentUnrecognized);

        Assert.Null(activity.ConversationId);
    }
    [Fact]
    public async Task TheTimelineReturnsOnlyTheRunsOfTheConversationAskedAbout()
    {
        // A run that belonged to no conversation is excluded rather than treated as matching:
        // a workspace demonstration is not part of somebody's conversation, however recent it is.
        var store = new InMemoryAgentActivityStore(NullLogger<InMemoryAgentActivityStore>.Instance);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        await store.RecordAsync(ActivityFor(mine, "mine"));
        await store.RecordAsync(ActivityFor(theirs, "theirs"));
        await store.RecordAsync(ActivityFor(null, "no conversation"));
        await store.RecordAsync(ActivityFor(mine, "mine again"));

        var conversation = await store.GetForConversationAsync(mine);

        // Oldest first, because a conversation is read in the order it happened.
        Assert.Equal(["mine", "mine again"], conversation.Select(a => a.Goal));
    }

    [Fact]
    public async Task TheTimelineIsNewestFirstBecauseThatIsHowATimelineIsRead()
    {
        // The two queries deliberately disagree: the workspace list is a timeline and is read
        // from the top down, while a conversation is read in the order it happened. This is what
        // stops the workspace from listing a person's most recent run at the bottom.
        var store = new InMemoryAgentActivityStore(NullLogger<InMemoryAgentActivityStore>.Instance);

        await store.RecordAsync(ActivityFor(null, "first"));
        await store.RecordAsync(ActivityFor(null, "second"));
        await store.RecordAsync(ActivityFor(null, "third"));

        var recent = await store.GetRecentAsync();

        Assert.Equal(["third", "second", "first"], recent.Select(a => a.Goal));
    }

    [Fact]
    public async Task TheWholeTimelineIsStillAvailableWhenNoConversationIsGiven()
    {
        // The workspace's own list covers runs started from the demonstrations, which carry no
        // conversation at all, so "everything" has to remain a supported question.
        var store = new InMemoryAgentActivityStore(NullLogger<InMemoryAgentActivityStore>.Instance);

        await store.RecordAsync(ActivityFor(Guid.NewGuid(), "in a conversation"));
        await store.RecordAsync(ActivityFor(null, "on its own"));

        var all = await store.GetRecentAsync(20);

        Assert.Equal(2, all.Count);
    }

    private static AgentActivity ActivityFor(Guid? conversationId, string goal) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            goal,
            DateTimeOffset.Now,
            AgentRequestSource.Text,
            AgentActivityStatus.Completed,
            conversationId);

    /// <summary>Writes a file, and forgets to say which switch governs it.</summary>
    private sealed class UndeclaredWriteTool : ITool
    {
        public string Name => "UndeclaredWriteTool";

        public string Description => "Writes a file.";

        public IReadOnlyList<string> RequiredInputs => [];

        public IReadOnlyList<AgentAction> DescribeActions(AgentStep step) =>
            [AgentAction.Write("Write a file", "Write a file.", PermissionCapability.FileWrite)];

        public Task<ToolResult> ExecuteAsync(ToolRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ToolResult.Success(Name, "written"));
    }

    /// <summary>Writes a file, and names the switch.</summary>
    private sealed class DeclaredWriteTool : ITool
    {
        public string Name => "DeclaredWriteTool";

        public string Description => "Writes a file.";

        public IReadOnlyList<string> RequiredInputs => [];

        public PermissionCapability? RequiredPermission => PermissionCapability.FileWrite;

        public IReadOnlyList<AgentAction> DescribeActions(AgentStep step) =>
            [AgentAction.Write("Write a file", "Write a file.", PermissionCapability.FileWrite)];

        public Task<ToolResult> ExecuteAsync(ToolRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ToolResult.Success(Name, "written"));
    }
}
