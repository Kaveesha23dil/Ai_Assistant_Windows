namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

/// <summary>
/// The retrieval configuration, bound from the <c>Rag</c> section.
/// <para>
/// These are the knobs of a relevance judgement, and every one of them is a value somebody could
/// reasonably disagree with. That is why they are configuration rather than constants written
/// into the ranker: whether 0.30 is the right bar for "close enough to bother showing" depends
/// on the documents and the questions, and a person with two hundred technical papers will want
/// a different answer from one with three letters.
/// </para>
/// <para>
/// The weights are exposed for the same reason. Vector search finds "authentication" when asked
/// about "log-in"; word search finds <c>JWT_REFRESH_TOKEN</c> where the model has averaged it
/// away. How much of the answer comes from each is a judgement about the corpus, not a constant
/// of the algorithm.
/// </para>
/// </summary>
public sealed class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>Gets whether retrieval-augmented answers are offered.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets how many passages a question retrieves by default.</summary>
    public int TopK { get; init; } = 8;

    /// <summary>
    /// Gets the ceiling on the context sent with one question, in characters. Roughly 7,500
    /// tokens, which leaves room inside a typical context window for a substantial answer as well
    /// as the passages.
    /// </summary>
    public int MaximumContextCharacters { get; init; } = 30_000;

    /// <summary>
    /// Gets how close a passage has to point the same way as the question before it is shown.
    /// <para>
    /// This is the setting that decides whether an answer is built from documents that mention
    /// the subject or from documents that are about it. Filling a quota with passages that merely
    /// clear the bar produces an answer that cites something irrelevant, which is worse than an
    /// answer that says nothing was found — so a passage below this line is dropped rather than
    /// padded in.
    /// </para>
    /// </summary>
    public double MinimumSimilarity { get; init; } = 0.30;

    /// <summary>
    /// Gets a value indicating whether wording counts alongside meaning. On by default, because
    /// the exact identifiers in technical documents are exactly the things a vector averages away.
    /// </summary>
    public bool UseHybridSearch { get; init; } = true;

    /// <summary>Gets how much a meaning match counts, relative to a wording match.</summary>
    public double VectorWeight { get; init; } = 0.75;

    /// <summary>Gets how much a wording match counts, relative to a meaning match.</summary>
    public double LexicalWeight { get; init; } = 0.25;

    /// <summary>
    /// Gets the extra credit a passage gets for appearing in a section whose title names the
    /// subject. "Deadline" in a heading is a stronger signal than the same word in a paragraph,
    /// because a heading is a claim about what follows it.
    /// </summary>
    public double SectionTitleWeight { get; init; } = 0.15;

    /// <summary>
    /// Gets the extra credit a passage gets from its file's name. Weaker than the section credit,
    /// since file names are chosen for filing rather than for content, but "Docker.md" really
    /// does mean the passage is about Docker.
    /// </summary>
    public double FileNameWeight { get; init; } = 0.10;

    /// <summary>
    /// Gets the most passages one document may contribute to a single answer.
    /// <para>
    /// Not zero, and not one. Five passages from a document that is clearly the only relevant
    /// source is the right answer; five from each of twenty is a document list. A cap keeps one
    /// long document from filling the whole context while still letting the most relevant
    /// document speak at length.
    /// </para>
    /// </summary>
    public int MaximumChunksPerDocument { get; init; } = 3;

    /// <summary>
    /// Gets how much two passages may overlap in wording before the second is treated as saying
    /// the same thing again. Chunks overlap by design, so without this a question would retrieve
    /// the same paragraph three times from three positions and spend its whole context on it.
    /// </summary>
    public double MaximumRedundancyRatio { get; init; } = 0.80;
}
