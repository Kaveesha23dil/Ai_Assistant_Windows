using WindowsAIAssistant.Application.Knowledge;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Tests.Knowledge;

/// <summary>
/// Covers the context a model is handed: what fits, what is left out, and what the labels say.
/// <para>
/// The budget is the contract. Exceed it and the request is refused by the provider, or the tail is
/// dropped by something further down, and the model answers from passages the person was never
/// shown were included. The label is the other half: a citation that does not name a file cannot
/// be checked, which is the only thing that makes a retrieved answer worth more than a confident
/// guess.
/// </para>
/// </summary>
public sealed class RagContextBuilderTests
{
    private readonly RagContextBuilder _builder = new();

    [Fact]
    public void AnEmptySetOfPassagesGivesAnEmptyContext()
    {
        var context = _builder.Build([], 1000);

        Assert.True(context.IsEmpty);
        Assert.Equal(string.Empty, context.Text);
        Assert.False(context.WasTruncated);
    }

    [Fact]
    public void AZeroBudgetGivesAnEmptyContextRatherThanFailing()
    {
        var context = _builder.Build([Passage("something")], 0);

        Assert.True(context.IsEmpty);
    }

    [Fact]
    public void APassageIsLabelledWithItsNumberAndItsFile()
    {
        var context = _builder.Build([Passage("termination requires notice.", fileName: "contract.pdf")], 1000);

        Assert.StartsWith("[Source 1: contract.pdf]", context.Text, StringComparison.Ordinal);
        Assert.Single(context.Sources);
    }

    [Fact]
    public void EachPassageGetsItsOwnNumber()
    {
        var context = _builder.Build(
            [
                Passage("first", fileName: "a.pdf"),
                Passage("second", fileName: "b.pdf"),
                Passage("third", fileName: "c.pdf"),
            ],
            1000);

        Assert.Contains("[Source 1: a.pdf]", context.Text, StringComparison.Ordinal);
        Assert.Contains("[Source 2: b.pdf]", context.Text, StringComparison.Ordinal);
        Assert.Contains("[Source 3: c.pdf]", context.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlaceInsideTheDocumentIsPartOfTheLabel()
    {
        // "Contract v3, page 4" is something somebody can open. "Contract v3" alone is a guess
        // about where to look, and a long document has many candidate places.
        var passage = Passage(
            "notice is thirty days",
            fileName: "contract.pdf",
            reference: new KnowledgeSourceReference { PageNumber = 4 });

        var context = _builder.Build([passage], 1000);

        Assert.Contains("page 4", context.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheDocumentsOwnFolderIsNeverSentWithThePassage()
    {
        // The path is in the index because a reindex needs it. A model has no use for it, and it
        // reveals the shape of somebody's disk — their account name, their client, their projects
        // — to a provider that did not need it.
        var context = _builder.Build([Passage("text", fileName: "contract.pdf")], 1000);

        Assert.DoesNotContain("Users", context.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\", context.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AContextNeverExceedsTheBudgetItWasGiven()
    {
        var passages = Enumerable.Range(0, 20)
            .Select(index => Passage(new string('x', 200), fileName: $"file-{index}.pdf"))
            .ToArray();

        foreach (var budget in new[] { 50, 120, 300, 700, 1500, 4000 })
        {
            var context = _builder.Build(passages, budget);

            // The budget is the whole contract, so it is checked at several sizes including the
            // awkward ones where a single block straddles it. The reported count is compared too:
            // a builder that overflowed the string but reported a compliant number would pass
            // everything above that only looked at the count.
            Assert.True(
                context.CharacterCount <= budget,
                $"A budget of {budget} produced {context.CharacterCount} characters.");
            Assert.Equal(context.Text.Length, context.CharacterCount);
        }
    }

    [Fact]
    public void TheLabelsAreCountedAgainstTheBudgetAsWellAsTheText()
    {
        // Charging only for the text would let a page of thirty short passages carry thirty file
        // names past the limit, which is where the overflow actually happens in practice.
        var passages = Enumerable.Range(0, 10)
            .Select(index => Passage("short", fileName: $"a-longer-file-name-{index}.pdf"))
            .ToArray();

        // Measured as passages accepted rather than characters, because the point is not that the
        // context is longer — it is that the labels are paid for out of the same budget. Given
        // exactly enough room for the text alone, a builder that ignored the labels would take
        // all ten.
        var textOnly = passages.Sum(passage => passage.Text.Length);
        var context = _builder.Build(passages, textOnly);

        Assert.True(
            context.Sources.Count < passages.Length,
            $"All {passages.Length} passages fitted a budget of {textOnly} characters, so the labels were not charged.");
    }

    [Fact]
    public void APassageThatWillNotFitIsLeftOutRatherThanCut()
    {
        // Half a clause is a sentence whose ending has been invented, and an answer built on one
        // is the failure this feature exists to avoid. The whole passage goes; the next one is
        // tried, because the ranking is not the same as the order.
        var passages = new[]
        {
            Passage(new string('a', 500), fileName: "long.pdf"),
            Passage("short and complete", fileName: "short.pdf"),
        };

        var context = _builder.Build(passages, 300);

        Assert.DoesNotContain("aaaa", context.Text, StringComparison.Ordinal);
        Assert.Contains("short and complete", context.Text, StringComparison.Ordinal);
        Assert.Equal("short.pdf", Assert.Single(context.Sources).FileName);
        Assert.True(context.WasTruncated);
    }

    [Fact]
    public void AContextThatHadToLeavePassagesOutSaysSo()
    {
        // Silently dropping passages would let somebody treat an answer drawn from the first two
        // documents as covering the whole base.
        var passages = Enumerable.Range(0, 10)
            .Select(index => Passage(new string('x', 300), fileName: $"f{index}.pdf"))
            .ToArray();

        Assert.True(_builder.Build(passages, 800).WasTruncated);
    }

    [Fact]
    public void AContextThatFitsEverythingDoesNotClaimToHaveBeenTruncated()
    {
        var context = _builder.Build(
            [Passage("one", fileName: "a.pdf"), Passage("two", fileName: "b.pdf")],
            10_000);

        Assert.False(context.WasTruncated);
        Assert.Equal(2, context.Sources.Count);
    }

    [Fact]
    public void TheFirstPassageIsCutRatherThanLosingTheQuestionItsAnswer()
    {
        // The one case where cutting is right. Returning an empty context to a question with a
        // relevant passage in it would report "your documents do not mention this" about a
        // document that does.
        var context = _builder.Build([Passage(new string('z', 2000), fileName: "huge.pdf")], 200);

        Assert.False(context.IsEmpty);
        Assert.Single(context.Sources);
        Assert.True(context.WasTruncated);
        Assert.True(context.CharacterCount <= 200);
    }

    [Fact]
    public void ACutPassageSaysThatItWasCut()
    {
        // A model given half a passage with no marker reads it as the whole of what the document
        // said, and will answer from it as though nothing is missing.
        var context = _builder.Build([Passage(new string('z', 2000), fileName: "huge.pdf")], 300);

        Assert.Contains("[truncated]", context.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACutPassageIsTheLastThingTheContextSays()
    {
        // The marker has to follow the text it refers to. Appended before it, it reads as a
        // statement about whatever came after — which is nothing.
        var context = _builder.Build([Passage(new string('z', 2000), fileName: "huge.pdf")], 300);

        Assert.EndsWith("[truncated]", context.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyPassageIsSkippedWithoutTakingUpANumber()
    {
        // A passage that turned out to be whitespace is not a source, and numbering it would make
        // the model's "[3]" refer to something that is not there.
        var context = _builder.Build(
            [
                Passage("real", fileName: "a.pdf"),
                Passage("   ", fileName: "blank.pdf"),
                Passage("also real", fileName: "b.pdf"),
            ],
            1000);

        Assert.Contains("[Source 2: b.pdf]", context.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("blank.pdf", context.Text, StringComparison.Ordinal);
        Assert.Equal(2, context.Sources.Count);
    }

    [Fact]
    public void TheSourcesAreThePassagesThatActuallyWentIn()
    {
        // The answer and the citation list are decided together, so a source that is listed but
        // not in the prompt is a citation to something the model never saw.
        var context = _builder.Build(
            [
                Passage("kept", fileName: "kept.pdf"),
                Passage(new string('q', 900), fileName: "dropped.pdf"),
            ],
            400);

        Assert.Equal("kept.pdf", Assert.Single(context.Sources).FileName);
        Assert.Contains("kept", context.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void APassageWithNoFileNameIsStillLabelled()
    {
        // A missing name should not produce a label that reads as a mistake; it should read as
        // what it is, a numbered source.
        var context = _builder.Build([Passage("text", fileName: string.Empty)], 1000);

        Assert.Contains("[Source 1]", context.Text, StringComparison.Ordinal);
    }

    private static KnowledgeSearchResult Passage(
        string text,
        string fileName = "document.pdf",
        KnowledgeSourceReference? reference = null) =>
        new()
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            FileName = fileName,
            FileType = DocumentFileType.Pdf,
            Text = text,
            SourceReference = reference ?? new KnowledgeSourceReference(),
            CombinedScore = 1d,
        };
}
