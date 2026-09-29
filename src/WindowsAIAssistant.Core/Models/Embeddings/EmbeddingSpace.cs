namespace WindowsAIAssistant.Core.Models.Embeddings;

/// <summary>
/// Identifies the space a set of vectors lives in: the provider that produced them, the model
/// that produced them, and how wide they are.
/// <para>
/// This exists because a vector is not a number that can be compared to any other number. Two
/// vectors can only be compared if the same model produced them, because the whole of a
/// model's meaning is the arrangement of its dimensions. Comparing a 1,536-wide vector from
/// one model against a 384-wide vector from another does not produce a wrong answer so much as
/// no answer, and a search that quietly did it would return whatever arithmetic allowed rather
/// than what it was asked for.
/// </para>
/// <para>
/// So the space is carried alongside the values, compared explicitly before any similarity is
/// computed, and written into the index with them. A document indexed under one model and then
/// searched under another is a document that must be reindexed, not a document that quietly
/// answers wrongly.
/// </para>
/// </summary>
/// <param name="Provider">The name of the provider that produced the vectors.</param>
/// <param name="Model">The model identifier the provider used.</param>
/// <param name="Dimensions">How many values each vector holds.</param>
public sealed record EmbeddingSpace(string Provider, string Model, int Dimensions)
{
    /// <summary>
    /// Gets a value indicating whether two spaces can be compared at all.
    /// <para>
    /// The width is checked as well as the names, because a provider can change a model under
    /// a name that has not changed. Two vectors of different lengths have nothing to say to each
    /// other no matter what they are called.
    /// </para>
    /// </summary>
    public bool IsCompatibleWith(EmbeddingSpace? other) =>
        other is not null
        && string.Equals(Provider, other.Provider, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Model, other.Model, StringComparison.OrdinalIgnoreCase)
        && Dimensions == other.Dimensions;

    /// <summary>Gets a single comparable key, for logs and for equality checks that want one.</summary>
    public string Key => $"{Provider}/{Model}/{Dimensions}";

    /// <inheritdoc />
    public override string ToString() => Key;
}
