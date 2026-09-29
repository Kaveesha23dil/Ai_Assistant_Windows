namespace WindowsAIAssistant.Infrastructure.Knowledge;

/// <summary>
/// The tables of the knowledge index, in one string.
/// <para>
/// Everything is <c>IF NOT EXISTS</c>, so this script is both the first-run script and the
/// upgrade script. That is deliberate: a separate "create" and "migrate" pair is two places for
/// a column to be added to one and forgotten on the other, and the only cost of applying the
/// whole thing every time is a few microseconds once, at first use.
/// </para>
/// <para>
/// Three decisions in here are worth stating.
/// </para>
/// <para>
/// <b>Identifiers are text.</b> A <c>Guid</c> written as text sorts, compares, and indexes
/// predictably, and costs sixteen more bytes per row than an integer would. That is a cheap price
/// for never having to know which column of which table was declared as a blob by which build.
/// </para>
/// <para>
/// <b>The content hash is unique across the index, not per base.</b> A uniqueness constraint in
/// the database is what makes the duplicate check true rather than merely intended: two runs of
/// the same file at the same moment cannot both add it, whichever of them got there first. The
/// trade is that the same file cannot deliberately sit in two bases, and that is the behaviour
/// wanted — one document, one set of vectors, one answer.
/// </para>
/// <para>
/// <b>Cascades are declared, not left to each repository.</b> Removing a document removes its
/// passages here, in the schema, with the foreign keys switched on by
/// <see cref="KnowledgeDatabase"/>. A repository that forgot to delete them would leave the text
/// of a document somebody removed still sitting in the index, which is exactly the thing a
/// removal is supposed to prevent.
/// </para>
/// </summary>
internal static class KnowledgeSchema
{
    /// <summary>
    /// The statements that create the index. Applied in order; each is independent of the others.
    /// </summary>
    public const string CreateScript = """
        CREATE TABLE IF NOT EXISTS SchemaInfo
        (
            Id      INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
            Version INTEGER NOT NULL
        );

        INSERT OR IGNORE INTO SchemaInfo (Id, Version) VALUES (1, 0);

        CREATE TABLE IF NOT EXISTS KnowledgeBases
        (
            Id            TEXT    NOT NULL PRIMARY KEY,
            Name          TEXT    NOT NULL,
            Description   TEXT    NULL,
            DocumentCount INTEGER NOT NULL DEFAULT 0,
            ChunkCount    INTEGER NOT NULL DEFAULT 0,
            IsDefault     INTEGER NOT NULL DEFAULT 0,
            CreatedAt     TEXT    NOT NULL,
            UpdatedAt     TEXT    NOT NULL
        );

        -- Exactly one base may carry the flag. Enforced here rather than by the code that sets
        -- it, so a bug that promotes a second base fails loudly at the point of the write
        -- instead of leaving two "default" bases and three callers each picking one.
        CREATE UNIQUE INDEX IF NOT EXISTS UX_KnowledgeBases_Default
            ON KnowledgeBases (IsDefault)
            WHERE IsDefault = 1;

        CREATE TABLE IF NOT EXISTS Documents
        (
            Id                   TEXT    NOT NULL PRIMARY KEY,
            KnowledgeBaseId      TEXT    NOT NULL REFERENCES KnowledgeBases (Id) ON DELETE CASCADE,
            FileName             TEXT    NOT NULL,
            FilePath             TEXT    NOT NULL,
            FileType             INTEGER NOT NULL,
            FileSize             INTEGER NOT NULL,
            FileHash             TEXT    NOT NULL,
            ModifiedAt           TEXT    NULL,
            IndexedAt            TEXT    NULL,
            ChunkCount           INTEGER NOT NULL DEFAULT 0,
            Status               INTEGER NOT NULL,
            StatusMessage        TEXT    NULL,
            EmbeddingProvider    TEXT    NULL,
            EmbeddingModel       TEXT    NULL,
            EmbeddingDimensions  INTEGER NULL,
            AddedAt              TEXT    NOT NULL
        );

        -- The duplicate check, enforced by the database. Content rather than path, so the same
        -- file under two names is one document and a renamed file is not a new one.
        CREATE UNIQUE INDEX IF NOT EXISTS UX_Documents_FileHash ON Documents (FileHash);

        -- The search path's join, and the page's list, both filter on the base.
        CREATE INDEX IF NOT EXISTS IX_Documents_KnowledgeBaseId ON Documents (KnowledgeBaseId);

        CREATE TABLE IF NOT EXISTS Chunks
        (
            Id                    TEXT    NOT NULL PRIMARY KEY,
            DocumentId            TEXT    NOT NULL REFERENCES Documents (Id) ON DELETE CASCADE,
            KnowledgeBaseId       TEXT    NOT NULL,
            Sequence              INTEGER NOT NULL,
            Content               TEXT    NOT NULL,
            Sections              TEXT    NOT NULL,
            SourceReference       TEXT    NOT NULL,
            CharacterCount        INTEGER NOT NULL,
            ContentHash           TEXT    NOT NULL,
            Vector                BLOB    NULL,
            VectorIsNormalized    INTEGER NOT NULL DEFAULT 0,
            EmbeddingProvider     TEXT    NULL,
            EmbeddingModel        TEXT    NULL,
            EmbeddingDimensions   INTEGER NULL
        );

        -- Reindexing deletes a document's passages by document, and reading them back in order
        -- is the same access, so the two share one index.
        CREATE INDEX IF NOT EXISTS IX_Chunks_DocumentId ON Chunks (DocumentId, Sequence);

        -- Not unique. Two passages with identical text in different documents are legitimate and
        -- share an embedding, but each keeps its own row, because a passage belongs to a place.
        CREATE INDEX IF NOT EXISTS IX_Chunks_ContentHash ON Chunks (ContentHash);

        -- The vector scan. Scoped to a base and to documents that are searchable, which is what
        -- a filtered question becomes as SQL.
        CREATE INDEX IF NOT EXISTS IX_Chunks_KnowledgeBaseId ON Chunks (KnowledgeBaseId);
        """;
}
