using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Knowledge;

/// <summary>
/// Owns the knowledge index: where it lives, and the one place its tables are created.
/// <para>
/// The database sits under this user's local application data, never at a configured absolute
/// path. That is not a convenience — it is what keeps somebody's account name and folder layout
/// out of a file that gets copied between machines, and it is why
/// <see cref="KnowledgeBaseOptions.DatabaseFileName"/> is a name that is checked for path
/// separators rather than a path that is trusted.
/// </para>
/// <para>
/// Every statement that creates or changes a table is here and nowhere else. The repositories
/// read and write rows and never issue DDL, so the shape of the index is something that can be
/// read in one file and versioned in one number. There is no second connection string, and no
/// second place where a column could be added to one table and forgotten on the other.
/// </para>
/// <para>
/// Initialization happens once, on the first operation, under a lock. It has to be that way
/// rather than at startup because the application has no hosted services and no run-once hook,
/// and a constructor cannot await — and it has to be idempotent anyway, since several
/// repositories will call it and only the first should do the work.
/// </para>
/// </summary>
public sealed class KnowledgeDatabase : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// The schema version this build writes. Compared against what is stored in the file, so a
    /// database written by an older build is brought forward rather than being read on the
    /// assumption that the columns it happens to select all exist.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    private const string DefaultBaseTitle = "My documents";

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly ILogger<KnowledgeDatabase> _logger;
    private readonly KnowledgeBaseOptions _options;
    private readonly string _connectionString;
    private bool _initialized;
    private bool _disposed;

    public KnowledgeDatabase(
        IOptions<KnowledgeBaseOptions> options,
        ILogger<KnowledgeDatabase> logger,
        string? databasePath = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        DatabasePath = databasePath ?? ResolveDefaultPath(_options);

        // Cache=Shared and a pooled connection per call, rather than one connection held for the
        // life of the process. A single long-lived connection would serialize every search
        // behind every other search, and a desktop application has no reason to have one.
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    /// <summary>Gets where the index file is, resolved once so every operation agrees.</summary>
    public string DatabasePath { get; }

    /// <summary>Gets the name the default base is given the first time one is needed.</summary>
    public static string DefaultKnowledgeBaseName => DefaultBaseTitle;

    /// <summary>
    /// Opens a connection, creating the tables first if this is the first time they have been
    /// needed.
    /// <para>
    /// The caller disposes the connection. A connection is cheap to open against a pooled file,
    /// and one connection shared between the repositories would make a search and an index of
    /// the same document contend for it.
    /// </para>
    /// </summary>
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch (SqliteException ex)
        {
            // Reported as a stable code and a sentence with no path in it, because this message
            // may be shown in a document list and a person's folder layout is not theirs to
            // broadcast.
            _logger.LogError(ex, "The knowledge index could not be opened.");
            throw new KnowledgeException(
                "The knowledge index could not be opened. Another copy of the assistant may have it locked.",
                ErrorCodes.KnowledgeStorageUnavailable,
                ex);
        }
    }

    /// <summary>
    /// Creates the tables if they are not there and brings the schema up to date if it is behind.
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Write-ahead logging so a search can read while a document is being indexed. The
            // default rollback journal would block the second one behind the first, which is the
            // difference between reindexing a document in the background and freezing the page.
            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);

            // Off by default in SQLite, and the foreign keys are what make removing a document
            // remove its passages. Without this the cascade is silently skipped and the index
            // grows with the text of every document ever removed.
            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);

            var existingVersion = await ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);

            // Every statement uses IF NOT EXISTS, so applying the whole script again is harmless
            // and there is no separate "fresh install" path to keep in step with the "upgrade"
            // one.
            await ExecuteAsync(connection, KnowledgeSchema.CreateScript, cancellationToken).ConfigureAwait(false);

            if (existingVersion != CurrentSchemaVersion)
            {
                await ExecuteAsync(
                    connection,
                    $"UPDATE SchemaInfo SET Version = {CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)};",
                    cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;

            _logger.LogInformation(
                "The knowledge index was opened at schema version {SchemaVersion}.",
                CurrentSchemaVersion);
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "The knowledge index could not be created.");
            throw new KnowledgeException(
                "The knowledge index could not be prepared.",
                ErrorCodes.KnowledgeStorageUnavailable,
                ex);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    /// <summary>
    /// Reads the schema version, or zero for a database that has never been initialized.
    /// <para>
    /// A missing table is a first run, not a failure, which is the one case where swallowing the
    /// error is right.
    /// </para>
    /// </summary>
    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM SchemaInfo LIMIT 1;";

        try
        {
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is null or DBNull ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Works out where the file goes, from the platform's own per-user data directory.
    /// </summary>
    /// <remarks>
    /// This is the Windows build, so the known-folder API is asked for through the environment
    /// variable it publishes, and the local — not roaming — application data folder is the one
    /// used. A knowledge index is large, is machine-specific, and is of no use on another
    /// machine, so it has no business in a folder that follows somebody to another computer.
    /// </remarks>
    private static string ResolveDefaultPath(KnowledgeBaseOptions options)
    {
        var fileName = options.DatabaseFileName;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "knowledge.db";
        }

        // A file name that can escape its own directory is refused rather than sanitized. Making
        // the value safe would mean quietly writing somewhere other than where the configuration
        // said, which is worse than refusing to start.
        if (fileName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':']) >= 0
            || fileName.Contains("..", StringComparison.Ordinal))
        {
            throw new KnowledgeException(
                "The knowledge index file name is not a file name.",
                ErrorCodes.KnowledgeStorageUnavailable);
        }

        // The application folder is named exactly once. Building it here and appending it again
        // below produced "…\WindowsAIAssistant\WindowsAIAssistant\knowledge.db" on the machines
        // where LOCALAPPDATA is not set, which is precisely the path nobody would guess when a
        // test could not find the file it had just written.
        var root = Environment.GetEnvironmentVariable("LOCALAPPDATA");

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            // Nothing anywhere to put it. Failing here is right: a knowledge base that silently
            // wrote to the working directory would be a second store that nothing else knows
            // about, and the two would disagree.
            throw new KnowledgeException(
                "There is no local application data folder to keep the knowledge index in.",
                ErrorCodes.KnowledgeStorageUnavailable);
        }

        return Path.Combine(root, "WindowsAIAssistant", fileName);
    }

    /// <summary>
    /// Releases the lock and the pooled connections.
    /// <para>
    /// Present as well as <see cref="DisposeAsync"/> because a synchronous owner — a service
    /// provider built by a test, a short-lived console host — disposes synchronously, and a type
    /// that only understood <see cref="IAsyncDisposable"/> turns every one of those into a thrown
    /// exception on the way out rather than on the way in. There is no asynchronous work here:
    /// this type holds a connection string and a semaphore, not an open connection, so the two
    /// paths do the same work and the pool is cleared in both.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _initializationLock.Dispose();

        // Only this database's pool. ClearAllPools would also close connections belonging to
        // every other SQLite file in the process, which in this application means the settings
        // and conversation stores as well: disposing the knowledge index would break an
        // unrelated store that something else was still using. Clearing the one pool is what
        // actually releases the handle this object is responsible for, which is the reason it
        // is cleared at all — Windows will not delete the file while a pooled handle is open.
        ClearOwnPool();
    }

    /// <summary>
    /// Releases the pooled handles for this database alone.
    /// <para>
    /// The connection passed to <see cref="SqliteConnection.ClearPool(SqliteConnection)"/> is
    /// never opened; the pool is selected by its connection string, and a file-backed
    /// connection string is all that is needed to identify it.
    /// </para>
    /// </summary>
    private void ClearOwnPool()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            SqliteConnection.ClearPool(connection);
        }
        catch (SqliteException)
        {
            // The pool is being released as a favour to the file system. A database that will
            // not give its handles back must not turn a clean shutdown into a failed one.
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
