using System.Reflection;

namespace WindowsAIAssistant.Application.Tests.Knowledge;

/// <summary>
/// Covers the wording a spoken knowledge question is reduced to before it reaches the index.
/// <para>
/// The reduction is small and the consequences are not. A question that arrives mangled finds
/// nothing, and the answer it gets back is "your documents do not mention that" — which is
/// indistinguishable, from the outside, from a document that genuinely does not cover the
/// question. The person concludes their file is missing something it has.
/// </para>
/// </summary>
public sealed class KnowledgeVoiceQueryTests
{
    /// <summary>
    /// Reads the private reduction rather than duplicating it. The method is private because
    /// nothing else in the application is meant to depend on how a spoken question is phrased;
    /// testing it through reflection keeps that true while still testing it.
    /// </summary>
    private static string Clean(string question)
    {
        var method = typeof(WindowsAIAssistant.Application.Voice.Commands.Knowledge.KnowledgeVoiceHandler).GetMethod(
            "StripSourceWords",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        return (string)method!.Invoke(null, [question])!;
    }

    [Theory]
    [InlineData("What does my contract say about notice", "what does my contract notice")]
    [InlineData("look in my documents for the renewal date", "look the renewal date")]
    [InlineData("tell me about the notice period", "tell me the notice period")]
    [InlineData("from the knowledge base, what is the fee", "what is the fee")]
    [InlineData("what does it say regarding termination", "what does it say termination")]
    [InlineData("in my notes who is the contact", "who is the contact")]
    [InlineData("from my files, find the invoice number", "find the invoice number")]
    [InlineData("in the knowledge base what is the refund window", "what is the refund window")]
    public void TheWordsThatNameTheSourceAreTakenOut(string spoken, string expected) =>
        Assert.Equal(expected, Clean(spoken));

    [Theory]
    [InlineData("what information is in the contract", "what information is in the contract")]
    [InlineData("which platform do we use", "which platform do we use")]
    [InlineData("what is the format", "what is the format")]
    [InlineData("what are the conditions beforehand", "what are the conditions beforehand")]
    [InlineData("what is the performance review", "what is the performance review")]
    [InlineData("what is the fee schedule", "what is the fee schedule")]
    [InlineData("where is the conference centre", "where is the conference centre")]
    public void AWordThatMerelyContainsAPhraseKeepsItsLetters(string spoken, string expected) =>
        // "information" ends in "on", "platform" and "format" and "beforehand" and "performance"
        // contain "for", "conditions" and "conference" contain "on". Removing the phrase from
        // inside the word would leave the index searching for "infrmati" and "plaform", find
        // nothing, and report that the document did not mention it.
        Assert.Equal(expected, Clean(spoken));

    [Fact]
    public void AStandaloneFramingWordIsStillRemovedFromInsideALongerQuestion() =>
        Assert.Equal("what is the information termination", Clean("what is the information for termination"));

    [Fact]
    public void AQuestionMadeOnlyOfSourceWordsIsLeftEmptyRatherThanHalfCleaned() =>
        Assert.Equal(string.Empty, Clean("in my documents"));

    [Fact]
    public void TheWordsAreRemovedWhereverTheyAppear() =>
        // "on" and "for" are ordinary words in a real question, and the reduction has to cope with
        // a person saying them in two places in one sentence.
        Assert.Equal("renewal and renewal", Clean("renewal on my documents and renewal for my documents"));
}
