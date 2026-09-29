using Microsoft.Data.Sqlite;
using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Embeddings;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Tests.Knowledge;

/// <summary>
/// Exercises the knowledge store against a real SQLite file: the shape of the data, the
/// transactions, and the constraints that keep a passage pointing at a document that exists.
/// <para>
/// The assertions are about what a person would be misled by if it were wrong. A cascade that
/// removed another document's passages would answer a question from the wrong file. A replacement
/// that left the old passages behind would answer twice from contradictory versions of the same
/// text. A count taken from the row instead of from the passages would put a number on screen that
/// nobody counted.
/// </para>
/// </summary>
public sealed class KnowledgeStoreTests
{
    [Fact]
    public async Task ABaseIsCreatedOnDemandRatherThanAtStartup()
    {
        using var store = KnowledgeStoreHarness.Create();

        // A fresh store holds no base at all. The default is created when somebody asks a
        // question, so a person who never uses the feature does not accumulate a base they never
        // made, and one deleted on purpose stays deleted until it is asked for again.
        Assert.Empty(await store.Bases.ListAsync());

        var created = await store.Bases.GetOrCreateDefaultAsync();

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Single(await store.Bases.ListAsync());
    }

    [Fact]
    public async Task AskingForTheDefaultBaseTwiceReturnsTheSameOne()
    {
        using var store = KnowledgeStoreHarness.Create();

        var first = await store.Bases.GetOrCreateDefaultAsync();
        var second = await store.Bases.GetOrCreateDefaultAsync();

        // Two default bases would make "search my documents" ambiguous, and there would be no way
        // for the person who asked to tell which one they meant.
        Assert.Equal(first.Id, second.Id);
        Assert.Single(await store.Bases.ListAsync());
    }

    [Fact]
    public async Task RenamingABaseKeepsItsIdentityAndItsDocuments()
    {
        using var store = KnowledgeStoreHarness.Create();
        var knowledgeBase = await store.Bases.GetOrCreateDefaultAsync();
        var document = Document(knowledgeBase.Id, "notes.pdf", 1);

        await store.Documents.SaveAsync(document);

        await store.Bases.RenameAsync(knowledgeBase.Id, "Work", "Everything for the job");

        var renamed = await store.Bases.GetAsync(knowledgeBase.Id);

        Assert.NotNull(renamed);
        Assert.Equal(knowledgeBase.Id, renamed!.Id);
        Assert.Equal("Work", renamed.Name);
        Assert.Equal("Everything for the job", renamed.Description);
        Assert.Single(await store.Documents.ListAsync(knowledgeBase.Id));
    }

    [Fact]
    public async Task ABaseCarriesTheCountsOfWhatIsActuallyInIt()
    {
        using var store = KnowledgeStoreHarness.Create();
        var knowledgeBase = await store.Bases.GetOrCreateDefaultAsync();
        var document = Document(knowledgeBase.Id, "one.pdf", chunkCount: 3);

        await store.Documents.SaveAsync(document);
        await ReplaceAsync(store, document, [Chunk(knowledgeBase.Id, document, 0, "one"), Chunk(knowledgeBase.Id, document, 1, "two")]);

        await store.Bases.RefreshCountsAsync(knowledgeBase.Id);

        var refreshed = await store.Bases.GetAsync(knowledgeBase.Id);

        // Two passages exist, not the three the document row claimed. The count is a fact about
        // the index, recomputed from it, rather than a number the writer asserted.
        Assert.Equal(1, refreshed!.DocumentCount);
        Assert.Equal(2, refreshed.ChunkCount);
    }

    [Fact]
    public async Task RemovingABaseRemovesItsDocumentsAndPassages()
    {
        using var store = KnowledgeStoreHarness.Create();
        var doomed = await store.Bases.GetOrCreateDefaultAsync();
        var kept = await store.Bases.AddAsync(KnowledgeBase.Create("Other", DateTimeOffset.UtcNow));

        var survivor = Document(kept.Id, "kept.pdf", 1);
        var removed = Document(doomed.Id, "removed.pdf", 1);

        await store.Documents.SaveAsync(survivor);
        await store.Documents.SaveAsync(removed);
        await ReplaceAsync(store, survivor, [Chunk(kept.Id, survivor, 0, "keep me")]);
        await ReplaceAsync(store, removed, [Chunk(doomed.Id, removed, 0, "remove me")]);

        Assert.True(await store.Bases.DeleteAsync(doomed.Id));

        Assert.Empty(await store.Documents.ListAsync(doomed.Id));
        Assert.Single(await store.Documents.ListAsync(kept.Id));

        // The passage from the surviving document is still there. A cascade that reached past the
        // base would have taken it, and the other base would quietly stop answering questions.
        var survivors = await store.Chunks.ListAsync(survivor.Id);
        Assert.Single(survivors);
        Assert.Equal("keep me", survivors[0].Text);
    }

    [Fact]
    public async Task RemovingADocumentRemovesItsPassages()
    {
        using var store = KnowledgeStoreHarness.Create();
        var knowledgeBase = await store.Bases.GetOrCreateDefaultAsync();
        var document = Document(knowledgeBase.Id, "gone.pdf", 2);

        await store.Documents.SaveAsync(document);
        await ReplaceAsync(store, document, [Chunk(knowledgeBase.Id, document, 0, "one"), Chunk(knowledgeBase.Id, document, 1, "two")]);

        Assert.True(await store.Documents.RemoveAsync(document.Id));

        // The passages went with it. The file itself is the person's, and removing something from
        // an index is a statement about the index rather than about their files, so nothing here
        // was told to touch a path.
        Assert.Empty(await store.Chunks.ListAsync(document.Id));
    }

    [Fact]
    public async Task ReplacingADocumentLeavesNoPassagesFromTheVersionItReplaced()
    {
        using var store = KnowledgeStoreHarness.Create();
        var knowledgeBase = await store.Bases.GetOrCreateDefaultAsync();
        var document = Document(knowledgeBase.Id, "changed.pdf", 2);

        await store.Documents.SaveAsync(document);
        await ReplaceAsync(store, document, [Chunk(knowledgeBase.Id, document, 0, "old one"), Chunk(knowledgeBase.Id, document, 1, "old two")]);

        // The reindex case. Two passages go, one arrives, and the old ones must not survive: a
        // retrieval that found both could answer from a sentence the file no longer contains,
        // which is the one failure a knowledge base cannot recover from on its own.
        await ReplaceAsync(store, document, [Chunk(knowledgeBase.Id, document, 0, "new one")]);

        var remaining = await store.Chunks.ListAsync(document.Id);

        Assert.Single(remaining);
        Assert.Equal("new one", remaining[0].Text);
        Assert.Equal(1, await store.Chunks.CountAsync(document.Id));
    }

    [Fact]
    public async Task ReplacingADocumentWithNothingLeavesItWithNoPassages()
    {
        using var store = KnowledgeStoreHarness.Create();
        var knowledgeBase = await store.Bases.GetOrCreateDefaultAsync();
        var document = Document(knowledgeBase.Id, "emptied.pdf", 1);

        await store.Documents.SaveAsync(document);
        await ReplaceAsync(store, document, [Chunk(knowledgeBase.Id, document, 0, "was here")]);

        // A file that re-reads as empty has to end up empty. Leaving the previous passages would
        // mean the index answered for a document that no longer says anything at all.
        await ReplaceAsync(store, document, []);

        Assert.Empty(await store.Chunks.ListAsync(document.Id));
    }

    [Fact]
    public async Task PassagesAreListedInTheOrderTheyAppearInTheDocument()
    {
        using var store = KnowledgeStoreHarness.Create();
        var knowledgeBase = await store.Bases.GetOrCreateDefaultAsync();
        var document = Document(knowledgeBase.Id, "ordered.pdf", 3);

        await store.Documents.SaveAsync(document);

        // Written out of order on purpose. The listing is what a context builder reads, and a
        // context whose passages run backwards produces one that ends mid-thought and opens with
        // its own conclusion.
        await ReplaceAsync(store, document, [
            Chunk(knowledgeBase.Id, document, 2, "third"),
            Chunk(knowledgeBase.Id, document, 0, "first"),
            Chunk(knowledgeBase.Id, document, 1, "second"),
        ]);

        var listed = await store.Chunks.ListAsync(document.Id);

        Assert.Equal(["first", "second", "third"], listed.Select(chunk => chunk.Text));
    }

    [Fact]
    public async Task AVectorSurvivesBeingWrittenAndReadBack()
    {
        using var store = KnowledgeStoreHarness.Create();
        var knowledgeBase = await store.Bases.GetOrCreateDefaultAsync();
        var document = Document(knowledgeBase.Id, "vectors.pdf", 1);

        await store.Documents.SaveAsync(document);

        var values = new float[] { 0.6f, 0.8f, 0f };
        await ReplaceAsync(store, document, [
            Chunk(knowledgeBase.Id, document, 0, "text", new EmbeddingVector(values, "fixed-model", "Test", normalize: true)),
        ]);

        var stored = await store.Chunks.ListAsync(document.Id);

        Assert.Single(stored);
        Assert.NotNull(stored[0].Embedding);

        // Compared value by value. A serializer that lost the length, the trailing components, or
        // the model's identity would still read back as something plausible, and a cosine score
        // against it would be quietly wrong rather than obviously broken.
        var readBack = stored[0].Embedding!;

        Assert.Equal(values, readBack.Values.ToArray());
        Assert.Equal(KnowledgeStoreHarness.TestSpace, readBack.Space);
    }

    [Fact]
    public async Task ADamagedVectorCostsTheVectorAndNotThePassage()
    {
        using var store = KnowledgeStoreHarness.Create();
        var knowledgeBase = await store.Bases.GetOrCreateDefaultAsync();
        var document = Document(knowledgeBase.Id, "corrupt.pdf", 2);

        await store.Documents.SaveAsync(document);
        await ReplaceAsync(store, document, [
            Chunk(knowledgeBase.Id, document, 0, "readable", new EmbeddingVector([1f, 0f, 0f], "fixed-model", "Test", normalize: true)),
            Chunk(knowledgeBase.Id, document, 1, "damaged", new EmbeddingVector([0f, 1f, 0f], "fixed-model", "Test", normalize: true)),
        ]);

        // What a partial write or a file copied between machines looks like: a blob that is not a
        // vector. One damaged row must not fail the whole question, and the passage is still worth
        // keeping, because its text can still be found by wording and the document can be
        // reindexed.
        using (var connection = store.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Chunks SET Vector = $blob WHERE DocumentId = $id AND Sequence = 1";
            command.Parameters.AddWithValue("$blob", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
            command.Parameters.AddWithValue("$id", document.Id.ToString());
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var listed = await store.Chunks.ListAsync(document.Id);

        Assert.Equal(2, listed.Count);
        Assert.Equal("damaged", listed[1].Text);
        Assert.Null(listed[1].Embedding);
        Assert.NotNull(listed[0].Embedding);

        // And it is left out of the scoring pass rather than scored as a zero vector, which would
        // rank it as if it matched nothing and mean anything equally.
        var scored = await store.Chunks.ListVectorsAsync(knowledgeBase.Id);
        Assert.Single(scored);
        Assert.Equal(0, scored[0].Sequence);
    }

    [Fact]
    public async Task TheScoringPassCarriesTheFileDetailsACitationNeeds()
    {
        using var store = KnowledgeStoreHarness.Create();
        var knowledgeBase = await store.Bases.GetOrCreateDefaultAsync();
        var document = Document(knowledgeBase.Id, "slides.pdf", 1);

        await store.Documents.SaveAsync(document);
        await ReplaceAsync(store, document, [
            Chunk(knowledgeBase.Id, document, 0, "content", new EmbeddingVector([1f, 0f, 0f], "fixed-model", "Test", normalize: true)),
        ]);

        var vectors = await store.Chunks.ListVectorsAsync(knowledgeBase.Id);

        // The score, the passage, and the file it came from are read in separate passes, so the
        // row that ranks has to already say which file it belongs to. A citation assembled after
        // this point from a lookup that has to guess would mislabel the source.
        var single = Assert.Single(vectors);
        Assert.Equal("slides.pdf", single.FileName);
        Assert.Equal(DocumentFileType.Pdf, single.FileType);
        Assert.Equal(document.Id, single.DocumentId);
    }

    [Fact]
    public async Task PassagesCanBeReadBackTogetherByIdentifier()
    {
        using var store = KnowledgeStoreHarness.Create();
        var knowledgeBase = await store.Bases.GetOrCreateDefaultAsync();
        var first = Document(knowledgeBase.Id, "first.pdf", 2);
        var second = Document(knowledgeBase.Id, "second.pdf", 1);

        await store.Documents.SaveAsync(first);
        await store.Documents.SaveAsync(second);
        await ReplaceAsync(store, first, [Chunk(knowledgeBase.Id, first, 0, "one"), Chunk(knowledgeBase.Id, first, 1, "two")]);
        await ReplaceAsync(store, second, [Chunk(knowledgeBase.Id, second, 0, "three")]);

        var wanted = (await store.Chunks.ListAsync(first.Id)).Select(chunk => chunk.Id).ToArray();
        var read = await store.Chunks.GetManyAsync(wanted);

        // The two documents' passages read back in one pass, which is how a retrieval that ranked
        // across several files fetches the text of the few that survived.
        Assert.Equal(2, read.Count);
        Assert.Equal(["one", "two"], wanted.Select(id => read[id].Text));
    }

    [Fact]
    public async Task AChangedFileIsFoundByWhatItNowContains()
    {
        using var store = KnowledgeStoreHarness.Create();
        var knowledgeBase = await store.Bases.GetOrCreateDefaultAsync();

        await store.Documents.SaveAsync(Document(knowledgeBase.Id, "under-a-name.pdf", 1));
        await store.Documents.SaveAsync(Document(knowledgeBase.Id, "under-another-name.pdf", 1));

        // The same file added twice under two names. The duplicate check searches on content
        // rather than on path, so the person is told instead of being left to wonder why the same
        // answer comes back from two sources.
        var found = await store.Documents.FindByHashAsync("hash-under-a-name.pdf");

        Assert.NotNull(found);
        Assert.Equal("under-a-name.pdf", found!.FileName);
    }

    [Fact]
    public async Task AFileThatChangedOnDiskIsMarkedOutdatedRatherThanReindexed()
    {
        using var store = KnowledgeStoreHarness.Create();
        var knowledgeBase = await store.Bases.GetOrCreateDefaultAsync();
        var document = Document(knowledgeBase.Id, "edited.pdf", 1);

        await store.Documents.SaveAsync(document);
        await store.Documents.MarkOutdatedAsync(document.Id);

        var marked = await store.Documents.GetAsync(document.Id);

        // The file changed and nothing has been reindexed yet, so the page has to say the index is
        // behind the file. Silently answering from the old text would be a wrong answer, and
        // silently reindexing would be a surprise cost and a slow response.
        Assert.Equal(KnowledgeDocumentStatus.Outdated, marked!.Status);
    }

    [Fact]
    public async Task ADocumentWhoseVectorsAreFromAnotherModelIsMarkedForReindexing()
    {
        using var store = KnowledgeStoreHarness.Create();
        var knowledgeBase = await store.Bases.GetOrCreateDefaultAsync();
        var stale = Document(knowledgeBase.Id, "stale.pdf", 1, space: new EmbeddingSpace("Test", "old-model", 3));
        var current = Document(knowledgeBase.Id, "current.pdf", 1, space: KnowledgeStoreHarness.TestSpace);

        await store.Documents.SaveAsync(stale);
        await store.Documents.SaveAsync(current);

        var marked = await store.Documents.MarkReindexRequiredAsync(KnowledgeStoreHarness.TestSpace);

        // Applied to whole documents rather than to passages, because a document indexed with the
        // old model has no vector that can be compared with a new question, whichever of its
        // passages are asked for. Left unmarked, it would answer from nothing and look empty.
        Assert.Equal(1, marked);
        Assert.Equal(KnowledgeDocumentStatus.ReindexRequired, (await store.Documents.GetAsync(stale.Id))!.Status);
        Assert.Equal(KnowledgeDocumentStatus.Indexed, (await store.Documents.GetAsync(current.Id))!.Status);
    }

    [Fact]
    public async Task APassageForADocumentThatDoesNotExistIsRefused()
    {
        using var store = KnowledgeStoreHarness.Create();
        var unsaved = new KnowledgeDocument
        {
            Id = Guid.NewGuid(),
            KnowledgeBaseId = Guid.NewGuid(),
            FileName = "orphan.pdf",
            FilePath = @"C:\Users\someone\Documents\orphan.pdf",
            FileHash = "orphan",
            AddedAt = DateTimeOffset.UtcNow,
        };

        // The foreign key is what stops an orphaned passage sitting in the index forever,
        // reachable by a search and pointing at nothing. One that outlived its document would be
        // counted in the base's totals and never be removable through the page.
        await Assert.ThrowsAsync<SqliteException>(
            async () => await ReplaceAsync(store, unsaved, [
                Chunk(unsaved.KnowledgeBaseId, unsaved, 0, "orphan"),
            ]));
    }

    private static Task ReplaceAsync(KnowledgeStoreHarness store, KnowledgeDocument document, IReadOnlyList<KnowledgeChunk> chunks) =>
        store.Chunks.ReplaceForDocumentAsync(document.Id, chunks);

    private static KnowledgeDocument Document(
        Guid knowledgeBaseId,
        string fileName,
        int chunkCount,
        EmbeddingSpace? space = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            KnowledgeBaseId = knowledgeBaseId,
            FileName = fileName,
            FilePath = $@"C:\Users\someone\Documents\{fileName}",
            FileType = DocumentFileType.Pdf,
            FileHash = $"hash-{fileName}",
            FileSize = 1024,
            Status = KnowledgeDocumentStatus.Indexed,
            ChunkCount = chunkCount,
            EmbeddingSpace = space ?? KnowledgeStoreHarness.TestSpace,
            IndexedAt = DateTimeOffset.UtcNow,
            AddedAt = DateTimeOffset.UtcNow,
        };

    private static KnowledgeChunk Chunk(
        Guid knowledgeBaseId,
        KnowledgeDocument document,
        int sequence,
        string text,
        EmbeddingVector? vector = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            KnowledgeBaseId = knowledgeBaseId,
            Sequence = sequence,
            Text = text,
            ContentHash = $"chunk-{document.Id}-{sequence}",
            CharacterCount = text.Length,
            Embedding = vector,
        };
}
