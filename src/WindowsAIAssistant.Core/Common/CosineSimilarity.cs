using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Embeddings;

namespace WindowsAIAssistant.Core.Common;

/// <summary>
/// Compares two embedding vectors by how close their directions are.
/// <para>
/// Cosine similarity is the right measure here because it asks the only question retrieval
/// cares about — does this passage point the same way as the question — and is indifferent to
/// how long either vector is. A passage of 4,000 characters and a passage of 200 can both be
/// relevant to the same question, and a measure that punished the long one for being long would
/// return short passages for reasons that have nothing to do with the question.
/// </para>
/// <para>
/// Every check the arithmetic needs is made here rather than by the caller. Two vectors of
/// different widths, a vector with no direction at all, and a value that is not a number are all
/// refused with a stated reason, because the alternative is worse than a crash: a similarity
/// quietly computed from mismatched arrays reads a few floats past the end, produces a number
/// near some unrelated vector, and returns it as a ranked result a person will act on.
/// </para>
/// </summary>
public static class CosineSimilarity
{
    /// <summary>
    /// Compares two vectors that must share an embedding space.
    /// </summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <param name="allowDifferentSpaces">
    /// Whether to compare vectors from different models. Off by default, and left off by every
    /// caller in this application: the only thing that legitimately crosses models is an
    /// explicit migration, and that has to say so.
    /// </param>
    /// <returns>A score from -1 to 1, where 1 means the same direction.</returns>
    /// <exception cref="KnowledgeException">
    /// Thrown when the vectors come from different spaces, have different widths, are empty, or
    /// hold a value that is not a number.
    /// </exception>
    public static float Compute(
        EmbeddingVector left,
        EmbeddingVector right,
        bool allowDifferentSpaces = false)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (!allowDifferentSpaces && !left.Space.IsCompatibleWith(right.Space))
        {
            throw new KnowledgeException(
                "These vectors were produced by different embedding models, so they cannot be compared. Reindex the affected documents.",
                ErrorCodes.KnowledgeEmbeddingSpaceMismatch);
        }

        return Compute(left.Values.Span, right.Values.Span);
    }

    /// <summary>
    /// Compares two spans of values, refusing anything that cannot be compared.
    /// <para>
    /// Span-based rather than vector-based because the scan that matters reads vectors straight
    /// out of the database into a buffer, and copying each of a thousand of them into a
    /// <see cref="EmbeddingVector"/> first would allocate a thousand times to compute a thousand
    /// numbers.
    /// </para>
    /// </summary>
    /// <param name="left">The first vector's values.</param>
    /// <param name="right">The second vector's values.</param>
    /// <returns>A score from -1 to 1, where 1 means the same direction.</returns>
    /// <exception cref="KnowledgeException">
    /// Thrown when the widths differ, either span is empty, or a value is not a finite number.
    /// </exception>
    public static float Compute(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            throw new KnowledgeException(
                "An empty vector cannot be compared with anything.",
                ErrorCodes.KnowledgeEmbeddingInvalid);
        }

        if (left.Length != right.Length)
        {
            throw new KnowledgeException(
                $"Vectors of {left.Length} and {right.Length} values cannot be compared.",
                ErrorCodes.KnowledgeEmbeddingSpaceMismatch);
        }

        float dot = 0f;
        float leftSquares = 0f;
        float rightSquares = 0f;

        for (var index = 0; index < left.Length; index++)
        {
            var a = left[index];
            var b = right[index];

            if (!float.IsFinite(a) || !float.IsFinite(b))
            {
                throw new KnowledgeException(
                    "An embedding value was not a finite number, so the vectors cannot be compared.",
                    ErrorCodes.KnowledgeEmbeddingInvalid);
            }

            dot += a * b;
            leftSquares += a * a;
            rightSquares += b * b;
        }

        if (leftSquares <= 1e-24f || rightSquares <= 1e-24f)
        {
            // A vector of all zeros has no direction, so "how similar are these" has no answer.
            // Zero is returned, which is the neutral score, and is the only value that cannot be
            // mistaken for a real match. Callers that must not see a match at all filter on the
            // caller's own similarity threshold, and zero never clears one.
            return 0f;
        }

        var denominator = MathF.Sqrt(leftSquares) * MathF.Sqrt(rightSquares);
        var score = dot / denominator;

        // Floating point can push a pair of identical vectors a hair past 1 or a hair below -1.
        // A score outside the range would look like a defect in a result list, so it is clamped
        // rather than returned.
        return Math.Clamp(score, -1f, 1f);
    }
}
