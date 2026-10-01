using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;
using WindowsAIAssistant.Application.Agents;

namespace WindowsAIAssistant.Application.Tests.Agents;

/// <summary>
/// Covers what hands a conversation over to the agent, and — more importantly — what does not.
/// <para>
/// The agent is opt-in from chat. Every case here where an ordinary sentence is <em>not</em>
/// matched matters as much as the ones that are, because the cost of a false positive is a
/// conversation sent down a path its author did not ask for: a plan on the screen instead of an
/// answer, a run recorded in a timeline nobody expected, and for a request that writes
/// something, a prompt nobody was reading when it appeared.
/// </para>
/// </summary>
public sealed class AgentRequestTriggerTests
{
    [Theory]
    [InlineData("agent: what is 17 times 23", "what is 17 times 23")]
    [InlineData("AGENT: find my documents", "find my documents")]
    [InlineData("  agent:   summarise notes.md  ", "summarise notes.md")]
    public void ThePrefixHandsOverExactlyWhatFollowsIt(string text, string expected)
    {
        Assert.True(AgentRequestTrigger.TryMatch(text, AgentRequestSource.Text, out var context));
        Assert.Equal(expected, context.Request);
        Assert.Equal(AgentRequestSource.Text, context.Source);
        Assert.Null(context.Scenario);
    }

    [Fact]
    public void ThePrefixIsRecordedAsSpokenWhenItWasSpoken()
    {
        // The source decides how the run is described in the timeline, and a dictated request is
        // worth being able to tell apart from a typed one.
        Assert.True(AgentRequestTrigger.TryMatch(
            "agent: find my documents",
            AgentRequestSource.Voice,
            out var context));

        Assert.Equal(AgentRequestSource.Voice, context.Source);
        Assert.True(context.WasSpoken);
    }

    [Theory]
    [InlineData("agent:")]
    [InlineData("agent:   ")]
    [InlineData("agent:\t")]
    public void APrefixWithNothingAfterItIsNotARequest(string text)
    {
        // A person who typed the word and then thought better of it has not asked anything, and
        // handing the agent an empty question would produce a run about nothing.
        Assert.False(AgentRequestTrigger.TryMatch(text, AgentRequestSource.Text, out _));
    }

    [Theory]
    [InlineData("run the report demonstration")]
    [InlineData("please run the report demo")]
    [InlineData("Show me the file search demonstration.")]
    public void ADemonstrationIsRecognisedWhenItIsNamedAsOne(string text)
    {
        Assert.True(AgentRequestTrigger.TryMatch(text, AgentRequestSource.Text, out var context));

        Assert.NotNull(context.Scenario);
        Assert.Equal(AgentRequestSource.DemoScenario, context.Source);
    }

    [Fact]
    public void ThePrefixWinsOverADemonstrationNamedInTheSameMessage()
    {
        // Someone who wrote "agent: ..." has said which they want, and the text after the colon
        // is their request. Re-interpreting it as a demonstration name would silently run
        // something other than what they typed.
        Assert.True(AgentRequestTrigger.TryMatch(
            "agent: the knowledge search demo",
            AgentRequestSource.Text,
            out var context));

        Assert.Null(context.Scenario);
        Assert.Equal("the knowledge search demo", context.Request);
    }

    [Fact]
    public void ADemonstrationIsMatchedByItsIdentifierSpelledAsWords()
    {
        // The identifiers are hyphenated in code, but nobody types a hyphen into a chat window.
        Assert.True(AgentRequestTrigger.TryMatch(
            "run the file search demonstration",
            AgentRequestSource.Text,
            out var context));

        Assert.Equal("file-search", context.Scenario!.Id);
    }

    [Theory]
    [InlineData("create a report")]
    [InlineData("what is 17 times 23")]
    [InlineData("find my project documents")]
    [InlineData("search my knowledge base for authentication")]
    [InlineData("agentic workflow automation")]
    [InlineData("")]
    [InlineData(null)]
    public void AnOrdinarySentenceIsLeftToTheChatPath(string? text)
    {
        // Each of these contains a word that appears in a scenario identifier, or a prefix-like
        // word, and none of them is asking for an agent. This is the property that keeps chat
        // working the way it did before the agent existed.
        Assert.False(AgentRequestTrigger.TryMatch(text, AgentRequestSource.Text, out _));
    }

    [Theory]
    [InlineData("show me the screen demo")]
    [InlineData("run the calculate demo")]
    [InlineData("the system info demonstration")]
    [InlineData("knowledge search demonstration")]
    public void EveryDemonstrationCanBeAskedForByName(string text)
    {
        Assert.True(AgentRequestTrigger.TryMatch(text, AgentRequestSource.Text, out var context));
        Assert.NotNull(context.Scenario);
    }

    [Fact]
    public void ADemonstrationWordAloneDoesNotRunOne()
    {
        // Naming the mode without naming a demonstration is a request to go and look at it, which
        // the navigation layer already handles. Guessing which demonstration was meant would
        // start a run nobody asked for.
        Assert.False(AgentRequestTrigger.TryMatch(
            "show me the demonstrations",
            AgentRequestSource.Text,
            out _));
    }

    [Fact]
    public void AnUnknownDemonstrationNameIsNotGuessedAt()
    {
        // Refused rather than matched to the nearest identifier, for the same reason the
        // navigation parser refuses an unknown tag: a wrong guess is worse than no answer.
        Assert.False(AgentRequestTrigger.TryMatch(
            "run the spreadsheet demonstration",
            AgentRequestSource.Text,
            out _));
    }

    [Fact]
    public void OnlyWholeWordsCountSoAPrefixInsideAWordDoesNot()
    {
        Assert.False(AgentRequestTrigger.TryMatch(
            "reagent: find my documents",
            AgentRequestSource.Text,
            out _));
    }

    [Fact]
    public void AWordThatMerelyContainsAnIdentifierIsNotAMatch()
    {
        // "reportage" is not the report demonstration, and a substring match would say it is.
        Assert.False(AgentRequestTrigger.TryMatch(
            "run the reportage demonstration",
            AgentRequestSource.Text,
            out _));
    }

    [Fact]
    public void TheTriggerReadsOnlyTheScenariosItIsGiven()
    {
        // The set of matchable names is supplied rather than hard-coded, so a test or a build
        // with a different set of demonstrations cannot silently match a name that is not on
        // screen. Nothing is named in the source except the built-in list.
        var custom = new DemoScenario("weather", "Report the weather", "prompt", "why", ["T"], []);

        Assert.True(AgentRequestTrigger.TryMatch(
            "run the weather demonstration",
            AgentRequestSource.Text,
            [custom],
            out var context));

        Assert.Same(custom, context.Scenario);
    }

    [Fact]
    public void TheRequestItProducesCarriesNoConversationUntilOneIsAttached()
    {
        // The caller decides which conversation a run belongs to, and the trigger has no way to
        // know it. A run is tagged by the chat that started it, not by the text that named the
        // agent.
        Assert.True(AgentRequestTrigger.TryMatch(
            "agent: what is 17 times 23",
            AgentRequestSource.Text,
            out var context));

        Assert.Null(context.ConversationId);

        var conversationId = Guid.NewGuid();

        Assert.Equal(conversationId, context.WithConversation(conversationId).ConversationId);
    }
}
