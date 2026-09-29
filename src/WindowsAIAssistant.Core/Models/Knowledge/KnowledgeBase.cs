namespace WindowsAIAssistant.Core.Models.Knowledge;

/// <summary>
/// A named collection of indexed documents that questions can be asked across.
/// <para>
/// Several can exist so a person's work, university, research, and personal notes stay
/// separate. That is not organizational decoration: the point of a knowledge base is that a
/// question reaches the right documents. One base holding everything means every question also
/// reaches the private ones, and a person who noticed would have no way to ask about their
/// project notes without also asking about their tax return.
/// </para>
/// <para>
/// The counts are maintained as documents are added and removed rather than counted on demand,
/// so the page can show what is indexed without a query per row. They are a display value, not
/// an authority: the document table is the truth, and a count that has drifted is a cosmetic
/// defect rather than a wrong answer.
/// </para>
/// </summary>
public sealed record KnowledgeBase
{
    /// <summary>Gets the identifier, stable for the life of the base.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the name a person chose.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets when the base was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets when the base was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Gets how many documents are in the base.</summary>
    public int DocumentCount { get; init; }

    /// <summary>Gets how many indexed chunks the base holds.</summary>
    public int ChunkCount { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is the base used when nobody names one.
    /// <para>
    /// Exactly one base carries the flag, enforced by the repository rather than assumed by
    /// callers: a knowledge page, a spoken "ask my documents", and the chat page's knowledge
    /// toggle all have to land on the same one without each of them remembering to ask.
    /// </para>
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>Creates a base, filling in the two timestamps.</summary>
    public static KnowledgeBase Create(
        string name,
        DateTimeOffset now,
        string? description = null,
        bool isDefault = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = Validate(name),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            IsDefault = isDefault,
        };

    private static string Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }
}
