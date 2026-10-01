using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;
using WindowsAIAssistant.Infrastructure.Agents;

namespace WindowsAIAssistant.Application.Tests.Helpers;

/// <summary>
/// An agent store over a real SQLite file, in a directory of its own.
/// <para>
/// These tests open an actual database rather than exercising the in-memory store twice. The
/// behaviour that matters here is SQLite's — that a uniqueness constraint replaces instead of
/// duplicating, that an upsert inside one statement is atomic, that a null column reads back as
/// null, and that a file survives the store object being thrown away. None of that is visible
/// from the in-memory implementation, and a test that used it would pass while the shipping
/// configuration did not.
/// </para>
/// <para>
/// Every harness gets a directory under the temp path and deletes it on dispose, so a test run
/// cannot read or write the store of whoever is running it.
/// </para>
/// </summary>
public sealed class AgentStoreHarness : IDisposable
{
    private readonly string _directory;
    private bool _disposed;

    private AgentStoreHarness(
        string directory,
        AgentDatabase database,
        SqliteAgentMemoryStore memories,
        SqliteAgentActivityStore activity)
    {
        _directory = directory;
        Database = database;
        Memories = memories;
        Activity = activity;
    }

    /// <summary>Gets the database, for tests that need to look at the file itself.</summary>
    public AgentDatabase Database { get; }

    /// <summary>Gets the memory store under test.</summary>
    public SqliteAgentMemoryStore Memories { get; }

    /// <summary>Gets the activity store under test.</summary>
    public SqliteAgentActivityStore Activity { get; }

    /// <summary>Gets where the file lives, for a test that checks the path rules.</summary>
    public string DatabasePath => Database.DatabasePath;

    /// <summary>Creates a harness with its own database file.</summary>
    public static AgentStoreHarness Create(int retention = 200, string? fileName = null)
    {
        var directory = NewDirectory();

        var path = Path.Combine(directory, fileName ?? "agent.db");

        return Build(directory, path, retention);
    }

    /// <summary>
    /// Creates a harness whose database path cannot be opened, by putting a directory where the
    /// file would go. This is what a store that cannot be written to looks like from the outside.
    /// </summary>
    public static AgentStoreHarness CreateUnwritable()
    {
        var directory = NewDirectory();

        // A directory occupies the path, so the file cannot be created beside it.
        Directory.CreateDirectory(Path.Combine(directory, "agent.db"));

        return Build(directory, Path.Combine(directory, "agent.db"), retention: 200);
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "waa-agent-store-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        return directory;
    }

    private static AgentStoreHarness Build(string directory, string path, int retention)
    {
        var options = Options.Create(new AgentOptions
        {
            DatabaseFileName = Path.GetFileName(path),
            ActivityRetentionCount = retention,
        });

        var database = new AgentDatabase(options, NullLogger<AgentDatabase>.Instance, path);

        return new AgentStoreHarness(
            directory,
            database,
            new SqliteAgentMemoryStore(database, NullLogger<SqliteAgentMemoryStore>.Instance),
            new SqliteAgentActivityStore(
                database,
                options,
                NullLogger<SqliteAgentActivityStore>.Instance));
    }

    /// <summary>
    /// Opens a second store over the same file, which is how a test stands in for a restart.
    /// </summary>
    public (SqliteAgentMemoryStore Memories, SqliteAgentActivityStore Activity) Reopen() =>
    (
        new SqliteAgentMemoryStore(Database, NullLogger<SqliteAgentMemoryStore>.Instance),
        new SqliteAgentActivityStore(
            Database,
            Options.Create(new AgentOptions { ActivityRetentionCount = 200 }),
            NullLogger<SqliteAgentActivityStore>.Instance));

    /// <summary>Builds a memory with the type and content a test needs.</summary>
    public static UserMemory Memory(
        AgentMemoryType type,
        string key,
        string value,
        DateTimeOffset? at = null) =>
        new(
            Guid.NewGuid(),
            type,
            key,
            value,
            at ?? DateTimeOffset.UtcNow,
            at ?? DateTimeOffset.UtcNow);

    /// <summary>Builds a run entry for a test.</summary>
    public static AgentActivity Run(
        string goal,
        DateTimeOffset startedAt,
        Guid? conversationId = null,
        AgentRequestSource source = AgentRequestSource.Text,
        Guid? runId = null) =>
        new(
            Guid.NewGuid(),
            runId ?? Guid.NewGuid(),
            goal,
            startedAt,
            source,
            AgentActivityStatus.Running,
            conversationId);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Database.Dispose();

        try
        {
            // The write-ahead log is a separate file from the database, and a directory that still
            // has one in it cannot be removed.
            foreach (var leftover in Directory.GetFiles(_directory))
            {
                File.Delete(leftover);
            }

            Directory.Delete(_directory);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }
}
