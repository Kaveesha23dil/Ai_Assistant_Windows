namespace WindowsAIAssistant.Infrastructure.Agents;

/// <summary>
/// The tables the agent keeps of itself, in one string.
/// <para>
/// Everything is <c>IF NOT EXISTS</c>, so this is both the first-run script and the upgrade
/// script, for the same reason the knowledge schema is written that way: two scripts means two
/// places for a column to be added to one and forgotten on the other, and the cost of applying
/// the whole thing on every first use is a few milliseconds once.
/// </para>
/// <para>
/// Two decisions here are the reason this file exists rather than two SQL strings in the
/// repositories, and both are about what the agent is allowed to keep.
/// </para>
/// <para>
/// <b>There is no column for content.</b> <c>Memories</c> holds a key, a value, and two
/// timestamps. <c>AgentActivity</c> holds a goal, tool names, a duration, a status, and counts.
/// There is nowhere to put a passage from a document, a frame of a screen, or a prompt sent to a
/// provider — not "the code is careful not to", but "there is no field", which is the only
/// version of that promise that survives somebody adding a feature in a hurry.
/// </para>
/// <para>
/// <b>Tool names are stored as a delimited list, not as rows.</b> A run's tools are always read
/// together, always in order, and never queried individually, so a join table would buy nothing
/// and cost a cascade. The separator is a control character that cannot occur in a tool name, so
/// the encoding round-trips.
/// </para>
/// </summary>
internal static class AgentSchema
{
    /// <summary>The statements that create the agent's tables. Applied in order.</summary>
    public const string CreateScript = """
        CREATE TABLE IF NOT EXISTS SchemaInfo
        (
            Id      INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
            Version INTEGER NOT NULL
        );

        INSERT OR IGNORE INTO SchemaInfo (Id, Version) VALUES (1, 0);

        CREATE TABLE IF NOT EXISTS Memories
        (
            Id          TEXT    NOT NULL PRIMARY KEY,
            MemoryType  TEXT    NOT NULL,
            Key         TEXT    NOT NULL,
            Value       TEXT    NOT NULL,
            CreatedAt   TEXT    NOT NULL,
            UpdatedAt   TEXT    NOT NULL,
            UseCount    INTEGER NOT NULL DEFAULT 0
        );

        -- The composite uniqueness constraint is the whole of the "replace, do not accumulate"
        -- rule, enforced by the database rather than by a read-then-write in the repository.
        -- A preference stated twice from two threads at the same moment produces one row with
        -- the second value, rather than two rows from which a reader picks whichever it finds
        -- first.
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Memories_Type_Key
            ON Memories (MemoryType, Key COLLATE NOCASE);

        CREATE TABLE IF NOT EXISTS AgentActivity
        (
            Id             TEXT    NOT NULL PRIMARY KEY,
            RunId          TEXT    NOT NULL,
            ConversationId TEXT    NULL,
            Goal           TEXT    NOT NULL,
            StartedAt      TEXT    NOT NULL,
            CompletedAt    TEXT    NULL,
            Source         TEXT    NOT NULL,
            Status         TEXT    NOT NULL,
            ToolsUsed      TEXT    NOT NULL DEFAULT '',
            DurationMs     INTEGER NOT NULL DEFAULT 0,
            SourceCount    INTEGER NOT NULL DEFAULT 0,
            Note           TEXT    NULL,
            ErrorCode      TEXT    NULL
        );

        -- The timeline is read newest-first and pruned oldest-first, and the conversation view
        -- filters by conversation, so both have an index. Without them the workspace's "recent
        -- runs" list becomes a full scan of every run the machine has ever run, which grows
        -- without bound on exactly the machine that has been using the agent the most.
        CREATE INDEX IF NOT EXISTS IX_AgentActivity_StartedAt
            ON AgentActivity (StartedAt DESC);

        CREATE INDEX IF NOT EXISTS IX_AgentActivity_Conversation
            ON AgentActivity (ConversationId, StartedAt);
        """;
}
