using WindowsAIAssistant.Core.Models.Embeddings;

namespace WindowsAIAssistant.Core.Abstractions.Knowledge;

/// <summary>
/// Turns a stored vector into bytes and back.
/// <para>
/// An interface rather than a static helper because the choice of encoding is a decision with
/// costs: little-endian floats are compact and fast to read in C#, and a base64 string is
/// compact and readable in a database browser but a third larger and slower to work with. Making
/// it a service means a different encoding can be introduced for a new store without the
/// repository or the search service changing, and it means the length-prefixed format that stops
/// a truncated blob from being read as a valid shorter vector lives in one place.
/// </para>
/// </summary>
public interface IVectorSerializer
{
    /// <summary>Writes a vector as bytes.</summary>
    byte[] Serialize(EmbeddingVector vector);

    /// <summary>
    /// Reads a vector back.
    /// <para>
    /// The space is supplied by the caller rather than read out of the bytes, because the bytes
    /// carry no model name: a float is a float, and only the caller knows which model it is
    /// being loaded as. A blob whose length does not match the space is refused rather than
    /// padded or truncated to fit.
    /// </para>
    /// </summary>
    EmbeddingVector Deserialize(ReadOnlySpan<byte> bytes, EmbeddingSpace space);
}
