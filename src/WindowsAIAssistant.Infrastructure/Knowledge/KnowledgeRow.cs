using System.Globalization;
using Microsoft.Data.Sqlite;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Embeddings;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Infrastructure.Knowledge;

/// <summary>
/// Turns rows into records, in one place.
/// <para>
/// Every repository needs the same conversions — a <c>Guid</c> stored as text, a timestamp
/// stored as text, an enum stored as an integer, a list stored as a delimited string — and
/// getting any of them subtly different in two repositories is how a document list starts
/// disagreeing with a search about what a row contains. So the conversions are written once here
/// and both repositories use them.
/// </para>
/// <para>
/// The timestamp format is the round-trip one, in UTC, with the kind and offset written out. That
/// is verbose and it is what makes reading a date back equal to writing one, which a bare
/// <c>ToString()</c> on a local time quietly stops being the moment somebody's machine moves
/// timezone.
/// </para>
/// </summary>
internal static class KnowledgeRow
{
    /// <summary>The columns the document and chunk writers need, in a fixed order.</summary>
    public const string DocumentColumns =
        "Id, KnowledgeBaseId, FileName, FilePath, FileType, FileSize, FileHash, ModifiedAt, " +
        "IndexedAt, ChunkCount, Status, StatusMessage, EmbeddingProvider, EmbeddingModel, " +
        "EmbeddingDimensions, AddedAt";

    public const string ChunkColumns =
        "Id, DocumentId, KnowledgeBaseId, Sequence, Content, Sections, SourceReference, " +
        "CharacterCount, ContentHash, Vector, VectorIsNormalized, EmbeddingProvider, " +
        "EmbeddingModel, EmbeddingDimensions";

    private const string SectionSeparator = "\u001f";

    public static Guid GetGuid(this SqliteDataReader reader, int ordinal) =>
        Guid.Parse(reader.GetString(ordinal));

    public static Guid? GetGuidOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));

    public static DateTimeOffset GetTimestamp(this SqliteDataReader reader, int ordinal)
    {
        var value = reader.GetString(ordinal);
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public static DateTimeOffset? GetTimestampOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(
                reader.GetString(ordinal),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);

    public static EmbeddingSpace? GetSpace(this SqliteDataReader reader, int provider, int model, int dimensions)
    {
        if (reader.IsDBNull(provider) || reader.IsDBNull(model) || reader.IsDBNull(dimensions))
        {
            return null;
        }

        return new EmbeddingSpace(
            reader.GetString(provider),
            reader.GetString(model),
            reader.GetInt32(dimensions));
    }

    public static IReadOnlyList<string> GetSections(this SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return [];
        }

        var value = reader.GetString(ordinal);
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        return value.Split(SectionSeparator, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Reads a document row, using the order given by <see cref="DocumentColumns"/>.</summary>
    public static KnowledgeDocument ReadDocument(this SqliteDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        KnowledgeBaseId = reader.GetGuid(1),
        FileName = reader.GetString(2),
        FilePath = reader.GetString(3),
        FileType = (DocumentFileType)reader.GetInt32(4),
        FileSize = reader.GetInt64(5),
        FileHash = reader.GetString(6),
        ModifiedAt = reader.GetTimestampOrNull(7),
        IndexedAt = reader.GetTimestampOrNull(8),
        ChunkCount = reader.GetInt32(9),
        Status = (KnowledgeDocumentStatus)reader.GetInt32(10),
        StatusMessage = reader.IsDBNull(11) ? null : reader.GetString(11),
        EmbeddingSpace = reader.GetSpace(12, 13, 14),
        AddedAt = reader.GetTimestamp(15),
    };

    /// <summary>Reads a chunk row, using the order given by <see cref="ChunkColumns"/>.</summary>
    public static KnowledgeChunk ReadChunk(this SqliteDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        DocumentId = reader.GetGuid(1),
        KnowledgeBaseId = reader.GetGuid(2),
        Sequence = reader.GetInt32(3),
        Text = reader.GetString(4),
        Sections = reader.GetSections(5),
        SourceReference = KnowledgeSourceReference.FromStorageString(
            reader.IsDBNull(6) ? null : reader.GetString(6)),
        CharacterCount = reader.GetInt32(7),
        ContentHash = reader.GetString(8),
        Embedding = null,
    };

    /// <summary>The space a chunk row's vector belongs to, without reading the vector itself.</summary>
    public static EmbeddingSpace? ReadChunkSpace(this SqliteDataReader reader) =>
        reader.GetSpace(11, 12, 13);

    public static string ToStorage(this DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static string? ToStorage(this DateTimeOffset? value) =>
        value?.ToStorage();

    public static string ToStorage(this IReadOnlyList<string> value) =>
        value.Count == 0 ? string.Empty : string.Join(SectionSeparator, value);

    /// <summary>
    /// A parameter for a list of identifiers, written as a parameter per value rather than as a
    /// joined string.
    /// <para>
    /// A joined string would be shorter to write and would be an injection point, because the
    /// only way to put values into it is to build it. One parameter per value cannot be anything
    /// but a value, and SQLite has a limit on how many of them there may be, which is checked and
    /// reported rather than being reached as a provider error from somewhere else.
    /// </para>
    /// </summary>
    public static List<SqliteParameter> CreateGuidParameters(
        this SqliteCommand command,
        string prefix,
        IReadOnlyCollection<Guid> values)
    {
        const int SQLiteParameterLimit = 900;

        if (values.Count > SQLiteParameterLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(values),
                values.Count,
                $"A single query cannot filter on more than {SQLiteParameterLimit} identifiers.");
        }

        var parameters = new List<SqliteParameter>(values.Count);

        foreach (var value in values)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"{prefix}{parameters.Count}";
            parameter.Value = value.ToString();
            command.Parameters.Add(parameter);
            parameters.Add(parameter);
        }

        return parameters;
    }
}
