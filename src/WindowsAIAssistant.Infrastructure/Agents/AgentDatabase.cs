using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Exceptions;

namespace WindowsAIAssistant.Infrastructure.Agents;

/// <summary>
/// Owns the agent's own small database: where it lives, and the one place its tables are created.
/// <para>
/// A separate file from the knowledge index, and deliberately. They have nothing in common but
/// a directory: one holds a vector per passage of somebody's documents, the other holds a
/// preference and a list of tool names. Merging them would mean that clearing the agent's
/// memory — which is a single button, and the kind of thing somebody reaches for when they want
/// the assistant to forget something — required touching a database whose other tables are full
/// of document text. Keeping them apart makes "forget" provably narrow.
/// </para>
/// <para>
/// The path is resolved from the platform's own per-user application data directory, never from
/// a configured absolute path. That is what keeps somebody's account name and folder layout out
/// of a file that gets copied between machines, and it is why
/// <see cref="AgentOptions.DatabaseFileName"/> is a name that is checked for path separators
/// rather than a path that is trusted.
/// </para>
/// <para>
/// Initialization happens once, on first use, under a lock, because there is no hosted service
/// and a constructor cannot await. Every repository here shares this object, so all of them see
/// the same file and only the first one pays for the schema.
/// </para>
/// </summary>
public sealed class AgentDatabase : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// The schema version this build writes, compared against what is stored in the file so a
    /// database written by an older build is brought forward rather than read on the assumption
    /// that every column it selects happens to exist.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly ILogger<AgentDatabase> _logger;
    private readonly string _connectionString;
    private bool _initialized;
    private bool _disposed;

    public AgentDatabase(
        IOptions<AgentOptions> options,
        ILogger<AgentDatabase> logger,
        string? databasePath = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        DatabasePath = databasePath ?? ResolveDefaultPath(options.Value);

        // Pooled connections opened per call, not one connection held for the life of the
        // process. A single long-lived handle would serialize every memory write behind every
        // timeline read, and there is no reason for a desktop application to have one.
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    /// <summary>Gets where the agent's file is, resolved once so every operation agrees.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Opens a connection, creating the tables first if this is the first time they have been
    /// needed. The caller disposes it.
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
            // A stable code and a sentence with no path in it: this message may be shown in a
            // timeline, and a person's folder layout is not theirs to broadcast.
            _logger.LogError(ex, "The agent database could not be opened.");
            throw new AgentException(
                "The agent could not open its own store. Another copy of the assistant may have it locked.",
                ErrorCodes.AgentMemoryUnavailable,
                ex);
        }
    }

    /// <summary>Creates the tables if they are not there, and brings the schema up to date.</summary>
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

            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);

            var existingVersion = await ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(connection, AgentSchema.CreateScript, cancellationToken).ConfigureAwait(false);

            if (existingVersion != CurrentSchemaVersion)
            {
                await ExecuteAsync(
                    connection,
                    $"UPDATE SchemaInfo SET Version = {CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)};",
                    cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;

            _logger.LogInformation(
                "The agent store was opened at schema version {SchemaVersion}.",
                CurrentSchemaVersion);
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "The agent database could not be created.");
            throw new AgentException(
                "The agent could not prepare its own store.",
                ErrorCodes.AgentMemoryUnavailable,
                ex);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    /// <summary>
    /// Reads the schema version, or zero for a file that has never been initialized. A missing
    /// table is a first run rather than a failure, which is the one case where swallowing the
    /// error is right.
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
    /// <para>
    /// Local rather than roaming, for the same reason the knowledge index is: what the agent
    /// remembers is machine-specific and of no use on another computer, so it has no business in
    /// a folder that follows somebody to a different machine.
    /// </para>
    /// </summary>
    private static string ResolveDefaultPath(AgentOptions options)
    {
        var fileName = options.DatabaseFileName;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "agent.db";
        }

        // A name that can escape its own directory is refused rather than sanitized. Making the
        // value safe would mean quietly writing somewhere other than where the configuration
        // said, which is worse than refusing to start.
        if (fileName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':']) >= 0
            || fileName.Contains("..", StringComparison.Ordinal))
        {
            throw new AgentException(
                "The agent store file name is not a file name.",
                ErrorCodes.AgentMemoryUnavailable);
        }

        var root = Environment.GetEnvironmentVariable("LOCALAPPDATA");

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            // Nothing anywhere to put it. Failing here is right: a store that silently wrote to
            // the working directory would be a second store nothing else knows about, and the two
            // would disagree.
            throw new AgentException(
                "There is no local application data folder to keep the agent store in.",
                ErrorCodes.AgentMemoryUnavailable);
        }

        return Path.Combine(root, "WindowsAIAssistant", fileName);
    }

    /// <summary>
    /// Releases the lock and this database's pooled handles.
    /// <para>
    /// Present as well as <see cref="DisposeAsync"/> because a service provider built by a test
    /// disposes synchronously, and a type that only understood <c>IAsyncDisposable</c> would turn
    /// every one of those into a thrown exception on the way out. Only this database's pool is
    /// cleared: clearing all of them would also close the knowledge index and the settings store.
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
        ClearOwnPool();
    }

    /// <summary>
    /// Releases the pooled handles for this database alone. The connection passed to
    /// <see cref="SqliteConnection.ClearPool(SqliteConnection)"/> is never opened; the pool is
    /// selected by connection string, and a file-backed one is all that is needed to identify it.
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
            // The pool is being released as a favour to the file system. A database that will not
            // give its handles back must not turn a clean shutdown into a failed one.
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}