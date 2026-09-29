using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Embeddings;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Embeddings;
using WindowsAIAssistant.Core.Models.Knowledge;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using WindowsAIAssistant.Infrastructure.Knowledge;
using WindowsAIAssistant.Infrastructure.Windows;

namespace WindowsAIAssistant.Application.Tests.Helpers;

/// <summary>
/// A knowledge store backed by a real SQLite file in a temporary folder.
/// <para>
/// Real rather than in-memory, because the things most likely to be wrong in this feature are
/// the SQL: the transaction that has to replace a document's passages without leaving half of
/// them behind, the constraint that keeps a chunk pointing at a document that exists, and the
/// blob that has to round-trip a vector. A fake repository would agree with all of them
/// unconditionally and prove nothing.
/// </para>
/// <para>
/// The file is deleted afterwards, and the connection pools are cleared first, because on Windows
/// a pooled handle makes the delete fail — which is the same reason the database clears them.
/// </para>
/// </summary>
internal sealed class KnowledgeStoreHarness : IDisposable
{
    private const string TestProvider = "Test";
    private const string TestModel = "fixed-model";
    private const int TestDimensions = 3;

    private readonly string _folder;
    private readonly string _path;
    private readonly KnowledgeDatabase _database;

    private KnowledgeStoreHarness(string folder, string path, KnowledgeDatabase database)
    {
        _folder = folder;
        _path = path;
        _database = database;
    }

    /// <summary>Gets the embedding space every vector in this store is written in.</summary>
    public static EmbeddingSpace TestSpace => new(TestProvider, TestModel, TestDimensions);

    public IKnowledgeBaseRepository Bases { get; private set; } = null!;

    public IKnowledgeDocumentRepository Documents { get; private set; } = null!;

    public IKnowledgeChunkRepository Chunks { get; private set; } = null!;

    public IVectorSerializer Serializer { get; private set; } = null!;

    public IVectorSearchService VectorSearch { get; private set; } = null!;

    /// <summary>Gets the path the store is using, for a test that opens it directly.</summary>
    public string DatabasePath => _path;

    public static KnowledgeStoreHarness Create()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wa-knowledge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "knowledge.db");
        var clock = new SystemDateTimeProvider();
        var logger = NullLogger<KnowledgeDatabase>.Instance;

        var database = new KnowledgeDatabase(
            Options.Create(new KnowledgeBaseOptions { DatabaseFileName = path }),
            logger,
            path);

        var serializer = new Float32VectorSerializer();
        var chunks = new SqliteKnowledgeChunkRepository(database, serializer, NullLogger<SqliteKnowledgeChunkRepository>.Instance);
        var embeddings = new FixedEmbeddingService(TestSpace);

        var harness = new KnowledgeStoreHarness(folder, path, database)
        {
            Serializer = serializer,
            Chunks = chunks,
            Bases = new SqliteKnowledgeBaseRepository(database, clock, NullLogger<SqliteKnowledgeBaseRepository>.Instance),
            Documents = new SqliteKnowledgeDocumentRepository(database, clock, NullLogger<SqliteKnowledgeDocumentRepository>.Instance),
            VectorSearch = new SqliteVectorSearchService(
                chunks,
                embeddings,
                Options.Create(new KnowledgeBaseOptions()),
                NullLogger<SqliteVectorSearchService>.Instance),
        };

        return harness;
    }

    /// <summary>Opens a connection the way the repositories do, for a test that inspects rows.</summary>
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        return connection;
    }

    /// <summary>Counts rows in a table, for a test that asserts on what was and was not deleted.</summary>
    public int CountRows(string table)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        _database.Dispose();
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file still held open by a pooled handle is not worth failing a test over, and the
            // folder is under the temporary directory, which is somebody else's to clean.
        }
    }
}

/// <summary>
/// An embedding service that returns a fixed vector, so a test can place a passage at a known
/// distance from a known question without a network call or a paid request.
/// <para>
/// The same vector for every input, deliberately. A test that needs passages to rank differently
/// writes its own vectors through the chunk repository, and a provider that quietly derived a
/// different vector per text would turn every ranking assertion into a test of the provider
/// rather than of the ranking.
/// </para>
/// </summary>
internal sealed class FixedEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingSpace _space;

    public FixedEmbeddingService(EmbeddingSpace space) => _space = space;

    public EmbeddingProviderKind ActiveProvider => EmbeddingProviderKind.Local;

    public string? ActiveModel => _space.Model;

    public string? ResolvedModel => _space.Model;

    public EmbeddingSpace? ActiveSpace => _space;

    public bool IsLocalProvider => true;

    public bool IsAvailable => true;

    public bool IsCompatibleWith(EmbeddingSpace? space) => _space.IsCompatibleWith(space);

    public Task<EmbeddingVector> GenerateAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(Fixed());

    public Task<IReadOnlyList<EmbeddingVector>> GenerateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EmbeddingVector>>(texts.Select(_ => Fixed()).ToArray());

    /// <summary>Builds a vector along the first axis, which is enough to rank against.</summary>
    private EmbeddingVector Fixed()
    {
        var values = new float[_space.Dimensions];
        values[0] = 1f;

        return new EmbeddingVector(
            values,
            _space.Model,
            _space.Provider,
            normalize: true);
    }
}
