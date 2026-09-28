namespace WindowsAIAssistant.Application.Documents;

/// <summary>
/// The limits the document features work within, in one value.
/// <para>
/// The numbers themselves are configuration, and the configuration type belongs to the
/// infrastructure layer. This record is the application's own copy of them, mapped once at
/// startup, so that nothing here has to know how options are read. Handlers take the value
/// rather than reading configuration themselves, which is what makes their limits fixed for
/// the length of a request: a document read halfway through cannot have its maximum changed
/// under it by a settings save in another view.
/// </para>
/// <para>
/// Fixed in the same spirit is the chunk count per request. A person asking a question wants an
/// answer that arrives, and twenty passages is as much text as can be considered and still be
/// worth reading. Beyond that, a whole-document answer is a different thing to ask for and is
/// asked for by summarizing instead.
/// </para>
/// </summary>
public sealed record DocumentProcessingLimits
{
    public int MaximumCharactersPerRequest { get; init; } = 40_000;

    public int MaximumChunksPerRequest { get; init; } = 20;

    public int MaximumCombinedSummaries { get; init; } = 20;

    public int MaximumConcurrentChunkSummaries { get; init; } = 3;
}
