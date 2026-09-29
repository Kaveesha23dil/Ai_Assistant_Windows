using System.Buffers.Binary;
using System.Text;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Embeddings;

namespace WindowsAIAssistant.Infrastructure.Knowledge;

/// <summary>
/// Reads and writes a vector as a small binary blob.
/// <para>
/// Little-endian single-precision values, behind a short header. The header is the interesting
/// part: without it, a blob that was truncated by a half-finished write would be read back as a
/// shorter but perfectly valid vector, and the search would then rank everything against a
/// question by comparing a few hundred values of one model with a few hundred of another. With
/// it, the count of values is checked against the space and the length of the payload against the
/// count, so a damaged blob is refused instead of believed.
/// </para>
/// <para>
/// Base64 would have been the alternative, and it is readable in a database browser. It is also a
/// third larger and slower to convert, on the hottest path in the feature, and nothing in this
/// feature is read by a person.
/// </para>
/// </summary>
public sealed class Float32VectorSerializer : IVectorSerializer
{
    /// <summary>The four bytes every blob starts with: "KEV1".</summary>
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("KEV1");

    private const int HeaderLength = 12;

    /// <inheritdoc />
    public byte[] Serialize(EmbeddingVector vector)
    {
        ArgumentNullException.ThrowIfNull(vector);

        var values = vector.Values.Span;
        var payloadLength = values.Length * sizeof(float);
        var buffer = new byte[HeaderLength + payloadLength];

        Magic.CopyTo(buffer, 0);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), values.Length);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8), payloadLength);

        for (var index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(HeaderLength + (index * sizeof(float))), values[index]);
        }

        return buffer;
    }

    /// <inheritdoc />
    public EmbeddingVector Deserialize(ReadOnlySpan<byte> bytes, EmbeddingSpace space)
    {
        ArgumentNullException.ThrowIfNull(space);

        if (bytes.Length < HeaderLength)
        {
            throw new KnowledgeException(
                "A stored vector is too short to contain its header, so the index needs to be rebuilt.",
                Core.Common.ErrorCodes.KnowledgeVectorCorrupted);
        }

        if (!bytes[..Magic.Length].SequenceEqual(Magic))
        {
            throw new KnowledgeException(
                "A stored vector is not in the expected format, so the index needs to be rebuilt.",
                Core.Common.ErrorCodes.KnowledgeVectorCorrupted);
        }
        var dimensions = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..8]);
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..12]);

        if (dimensions != space.Dimensions)
        {
            throw new KnowledgeException(
                "A stored vector has a different number of values from the current embedding model, "
                    + "so the documents need to be reindexed.",
                Core.Common.ErrorCodes.KnowledgeEmbeddingSpaceMismatch);
        }

        var expectedPayloadLength = dimensions * sizeof(float);

        if (payloadLength != expectedPayloadLength || bytes.Length != HeaderLength + expectedPayloadLength)
        {
            throw new KnowledgeException(
                "A stored vector was truncated, so the index needs to be rebuilt.",
                Core.Common.ErrorCodes.KnowledgeVectorCorrupted);
        }

        var values = new float[dimensions];
        for (var index = 0; index < dimensions; index++)
        {
            values[index] = BinaryPrimitives.ReadSingleLittleEndian(
                bytes.Slice(HeaderLength + (index * sizeof(float)), sizeof(float)));
        }

        // The space says the values are already unit length, which is how they were written, so
        // the magnitude is not recalculated on the way out of the database.
        return EmbeddingVector.FromStored(values, space, normalized: true);
    }
}
