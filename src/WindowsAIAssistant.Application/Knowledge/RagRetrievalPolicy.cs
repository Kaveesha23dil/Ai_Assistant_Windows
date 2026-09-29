namespace WindowsAIAssistant.Application.Knowledge;

/// <summary>
/// The relevance judgement, as one value owned by this layer.
/// <para>
/// The numbers are configuration and the configuration type belongs to Infrastructure. This
/// record is the application's own copy of them, mapped once at startup, so nothing here has to
/// know how options are read — the same arrangement the document feature uses for its limits.
/// The ranker and the retriever take the value rather than an options monitor, which is what
/// makes one question's ranking fixed for its whole length: a settings save in another view
/// cannot change the threshold halfway through assembling an answer.
/// </para>
/// <para>
/// Every value here is one somebody could reasonably disagree with. Whether 0.30 is the right bar
/// for "close enough to bother showing" depends on the documents and the questions, and a person
/// with two hundred technical papers will want a different answer from one with three letters.
/// </para>
/// </summary>
public sealed record RagRetrievalPolicy
{
    /// <summary>Gets whether retrieval-augmented answers are offered at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets how many passages a question retrieves by default.</summary>
    public int TopK { get; init; } = 8;

    /// <summary>
    /// Gets the ceiling on the context sent with one question, in characters. Roughly 7,500
    /// tokens, which leaves room inside a typical context window for a substantial answer as well
    /// as the passages themselves.
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
    /// the exact identifiers in technical documents are exactly the things a vector averages away:
    /// <c>JWT_REFRESH_TOKEN</c> is findable by wording and nearly unfindable by meaning.
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
    /// Not zero, and not one. Several passages from a document that is clearly the only relevant
    /// source is the right answer; that many from each of twenty is a document list. The cap keeps
    /// one long document from filling the whole context while still letting the most relevant
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

    /// <summary>
    /// Gets how many times over the requested count the store is asked for candidates.
    /// <para>
    /// More than one, because the threshold, the redundancy pass, and the per-document cap each
    /// remove passages after the store has answered. Asking for exactly as many as will be used
    /// and then discarding half of them would mean the answer is built from whatever happened to
    /// survive being under-supplied.
    /// </para>
    /// </summary>
    public int CandidateMultiplier { get; init; } = 4;
}
