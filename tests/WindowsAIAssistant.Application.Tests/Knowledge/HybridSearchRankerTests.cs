using WindowsAIAssistant.Application.Knowledge;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Tests.Knowledge;

/// <summary>
/// Covers the step that turns two kinds of match into one ordered list a context can be built
/// from.
/// <para>
/// Everything after this step trusts it. A passage that should have been dropped because nothing
/// about it was relevant takes a slice of the budget and a number in the citation list, and the
/// model is asked to answer from it. A passage that should have won because it is the passage the
/// document was actually about is left out, and the answer is confidently wrong.
/// </para>
/// </summary>
public sealed class HybridSearchRankerTests
{
    private static readonly RagRetrievalPolicy Policy = new()
    {
        TopK = 8,
        MinimumSimilarity = 0.30,
        VectorWeight = 0.75,
        LexicalWeight = 0.25,
        SectionTitleWeight = 0.15,
        FileNameWeight = 0.10,
        MaximumChunksPerDocument = 3,
        MaximumRedundancyRatio = 0.80,
    };

    [Fact]
    public void NothingInMeansNothingOut()
    {
        var results = new HybridSearchRanker(Policy).Rank([], "anything", 5);

        Assert.Empty(results);
    }

    [Fact]
    public void AskingForNothingReturnsNothing()
    {
        var ranker = new HybridSearchRanker(Policy);

        Assert.Empty(ranker.Rank([Passage("notice", vectorScore: 0.9)], "notice", 0));
        Assert.Empty(ranker.Rank([Passage("notice", vectorScore: 0.9)], "notice", -1));
    }

    [Fact]
    public void TheClosestPassageComesFirst()
    {
        var results = new HybridSearchRanker(Policy).Rank(
            [
                Passage("mildly related", vectorScore: 0.40),
                Passage("about notice", vectorScore: 0.92),
                Passage("barely related", vectorScore: 0.35),
            ],
            "what does the contract say about notice",
            5);

        Assert.Equal("about notice", results[0].Text);
    }

    [Fact]
    public void APassageThatIsNothingLikeTheQuestionIsLeftOut()
    {
        // Better an answer that says nothing was found than one citing a passage that never
        // mentioned the subject. The threshold is allowed to return fewer than were asked for.
        var results = new HybridSearchRanker(Policy).Rank(
            [Passage("the office wifi password is on the fridge", vectorScore: 0.05)],
            "what does the contract say about notice",
            5);

        Assert.Empty(results);
    }

    [Fact]
    public void APageOfUnrelatedPassagesStillProducesOnlyTheOnesThatClearTheBar()
    {
        var passages = Enumerable.Range(0, 30)
            .Select(index => Passage($"unrelated sentence number {index} about {Guid.NewGuid()}", vectorScore: 0.31))
            .ToList();

        var results = new HybridSearchRanker(Policy).Rank(passages, "notice", 5);

        Assert.True(results.Count <= 5);
    }

    [Fact]
    public void OnlyAsManyPassagesAsWereAskedForComeBack()
    {
        var passages = Enumerable.Range(0, 20)
            .Select(index => Passage($"a distinct statement {Guid.NewGuid()} here", vectorScore: 0.9 - (index * 0.01)))
            .ToList();

        var results = new HybridSearchRanker(Policy).Rank(passages, "anything", 3);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void ARepeatedPassageIsNotCitedThreeTimes()
    {
        // Chunks overlap by design, so three passages can be one paragraph. Three copies spend
        // the whole context saying one thing and give the model one source of evidence for a
        // claim it is meant to support with several. They come from one document, because that
        // is the only place chunking repeats anything.
        var documentId = Guid.NewGuid();
        var text = "either party may terminate this agreement by giving thirty days written notice";
        var passages = new[]
        {
            Passage(text, vectorScore: 0.9, documentId: documentId),
            Passage(text, vectorScore: 0.89, documentId: documentId),
            Passage(text, vectorScore: 0.88, documentId: documentId),
        };

        var results = new HybridSearchRanker(Policy).Rank(passages, "termination", 5);

        Assert.Single(results);
    }

    [Fact]
    public void ADifferentDocumentIsNotTreatedAsARepetition()
    {
        // Two contracts that happen to share a clause are two sources, and dropping the second
        // would quietly discard the corroboration.
        var results = new HybridSearchRanker(Policy).Rank(
            [
                Passage("either party may terminate by giving thirty days notice", vectorScore: 0.9, documentId: Guid.NewGuid()),
                Passage("either party may terminate by giving thirty days notice", vectorScore: 0.88, documentId: Guid.NewGuid()),
            ],
            "termination",
            5);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void OneLongDocumentCannotFillTheWholeAnswer()
    {
        // A single book, chunked, matches everything weakly and would otherwise supply every
        // passage, leaving a citation that points at one file and says nothing about the rest of
        // the base.
        var documentId = Guid.NewGuid();

        var passages = Enumerable.Range(0, 8)
            .Select(index => Passage($"a genuinely different sentence number {index} {Guid.NewGuid()}", vectorScore: 0.85, documentId: documentId))
            .ToList();

        var results = new HybridSearchRanker(Policy).Rank(passages, "anything", 8);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void ThePassagesADocumentKeepsAreItsBestOnes()
    {
        // The cap is applied after the ordering, so it must not be the first three that happened
        // to be scanned.
        var documentId = Guid.NewGuid();

        var passages = new[]
        {
            Passage("weakest but distinct one here", vectorScore: 0.60, documentId: documentId),
            Passage("strongest and distinct text", vectorScore: 0.95, documentId: documentId),
            Passage("middle and also distinct", vectorScore: 0.80, documentId: documentId),
            Passage("another middle distinct one", vectorScore: 0.75, documentId: documentId),
        };

        var results = new HybridSearchRanker(Policy).Rank(passages, "anything", 8);

        Assert.Equal(3, results.Count);
        Assert.Equal("strongest and distinct text", results[0].Text);
        Assert.DoesNotContain(results, result => result.Text == "weakest but distinct one here");
    }

    [Fact]
    public void TheDocumentCapCanBeTurnedOff()
    {
        var documentId = Guid.NewGuid();
        var policy = Policy with { MaximumChunksPerDocument = 0 };

        var passages = Enumerable.Range(0, 6)
            .Select(index => Passage($"distinct sentence {index} {Guid.NewGuid()}", vectorScore: 0.85, documentId: documentId))
            .ToList();

        Assert.Equal(6, new HybridSearchRanker(policy).Rank(passages, "anything", 8).Count);
    }

    [Fact]
    public void APassageWithNoVectorStillCompetesOnItsWording()
    {
        // The degraded path, where the provider is unavailable and the store falls back to word
        // matching. A keyword find there must not be ranked below a vector find that matched
        // nothing, which is what a zero vector score would do to it.
        var results = new HybridSearchRanker(Policy).Rank(
            [
                Passage("nothing to do with the question", vectorScore: 0.32),
                Passage("notice notice notice termination termination", vectorScore: null),
            ],
            "notice termination",
            5);

        Assert.Equal(2, results.Count);
        Assert.Equal("notice notice notice termination termination", results[0].Text);
    }

    [Fact]
    public void APassageWithNoVectorSaysSoRatherThanReportingAZero()
    {
        // A zero would be read as "as far away as it is possible to be", which is a different
        // claim from "this was not scored by meaning at all".
        var results = new HybridSearchRanker(Policy).Rank(
            [Passage("notice termination", vectorScore: null)],
            "notice",
            5);

        Assert.False(Assert.Single(results).HasVectorScore);
    }

    [Fact]
    public void AHeadingThatNamesTheSubjectBreaksATie()
    {
        // Two passages equally close in meaning, one of them under a heading that names the
        // subject. That is the case a tie is for, and it is a small enough nudge that it cannot
        // carry a genuinely worse passage over the line.
        var results = new HybridSearchRanker(Policy).Rank(
            [
                Passage("the period runs from the first day of the month", vectorScore: 0.70, sections: ["General"]),
                Passage("the period runs from the first day of the month", vectorScore: 0.70, sections: ["Termination"]),
            ],
            "termination",
            5);

        // Both passages are kept — they are separate chunks — so the order is what says whether
        // the heading was noticed at all.
        Assert.Equal(2, results.Count);
        Assert.Equal("Termination", results[0].Sections[0]);
    }

    [Fact]
    public void AFileWhoseNameIsAskedAboutIsFavouredSlightly()
    {
        var results = new HybridSearchRanker(Policy).Rank(
            [
                Passage("content about the subject in hand", vectorScore: 0.70, fileName: "unrelated.pdf"),
                Passage("content about the subject in hand", vectorScore: 0.70, fileName: "notice.pdf"),
            ],
            "notice",
            5);

        Assert.Equal("notice.pdf", results[0].FileName);
    }

    [Fact]
    public void AHeadingAndAFileNameCannotCarryAnIrrelevantPassageOverTheLine()
    {
        // Both bonuses together are worth a quarter of the score, so a passage that means nothing
        // to the question cannot reach the bar on its headings alone.
        var results = new HybridSearchRanker(Policy).Rank(
            [
                Passage("a passage that is about nothing in the question", vectorScore: 0.20, fileName: "notice.pdf", sections: ["Termination"]),
            ],
            "notice",
            5);

        Assert.Empty(results);
    }

    [Fact]
    public void TheScoreThatComesBackIsTheOneThePassageWasRankedBy()
    {
        var results = new HybridSearchRanker(Policy).Rank(
            [Passage("first distinct text", vectorScore: 0.90), Passage("second distinct text", vectorScore: 0.50)],
            "anything",
            5);

        Assert.Equal(results[0].CombinedScore, results[1].CombinedScore + (results[0].CombinedScore - results[1].CombinedScore));

        for (var index = 1; index < results.Count; index++)
        {
            Assert.True(results[index - 1].CombinedScore >= results[index].CombinedScore);
        }
    }

    [Fact]
    public void TheOrderIsTheSameWhenTwoPassagesScoreIdentically()
    {
        // A stable tie-break, so the same question returns the same order twice. Without one, a
        // sort that is merely stable by accident gives a different context run to run, and two
        // answers to the same question differ for no reason anybody can see.
        var first = Passage("identical text for the tie", vectorScore: 0.80);
        var second = Passage("identical text for the tie", vectorScore: 0.80);

        var results = new HybridSearchRanker(Policy).Rank([first, second], "tie", 1);

        Assert.Single(results);
    }

    [Fact]
    public void AQuestionWithNoUsableWordsDoesNotCrashTheRanking()
    {
        var results = new HybridSearchRanker(Policy).Rank(
            [Passage("some text with words", vectorScore: 0.80)],
            "!!! ???",
            5);

        Assert.Single(results);
    }

    [Fact]
    public void AnEmptyPassageIsNotReturned()
    {
        // A passage with no words cannot be scored and cannot be quoted, and it would still
        // consume a number in the citation list.
        var results = new HybridSearchRanker(Policy).Rank(
            [Passage("   ", vectorScore: 0.95)],
            "anything",
            5);

        Assert.Empty(results);
    }

    private static KnowledgeSearchResult Passage(
        string text,
        double? vectorScore,
        Guid? documentId = null,
        string fileName = "document.pdf",
        IReadOnlyList<string>? sections = null) =>
        new()
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = documentId ?? Guid.NewGuid(),
            FileName = fileName,
            FileType = DocumentFileType.Pdf,
            Text = text,
            Sections = sections ?? [],
            VectorScore = vectorScore ?? double.NaN,
        };
}
