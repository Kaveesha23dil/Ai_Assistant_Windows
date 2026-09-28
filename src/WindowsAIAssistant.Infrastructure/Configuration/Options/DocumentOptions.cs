namespace WindowsAIAssistant.Infrastructure.Configuration.Options;

/// <summary>
/// The limits and switches that govern document reading.
/// <para>
/// Every value here exists to bound work. Reading a document is the one operation in this
/// application whose size is chosen by whoever sent the file rather than by us, so these
/// limits are what stand between a 40-megabyte spreadsheet and an application that stops
/// responding. They are configuration rather than constants so that a person who needs to work
/// with a large document can raise them deliberately, and see that they have.
/// </para>
/// </summary>
public sealed class DocumentOptions
{
    /// <summary>The configuration section these values are bound from.</summary>
    public const string SectionName = "Documents";

    /// <summary>Gets whether document intelligence is offered at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the largest file that will be opened, in megabytes. Checked before parsing, so an
    /// oversized file is refused rather than partially read.
    /// </summary>
    public int MaximumFileSizeMb { get; init; } = 50;

    /// <summary>
    /// Gets the most text that will be kept from one document. Everything past this is dropped
    /// and a warning is added, so a person is told their document was only partly read.
    /// </summary>
    public int MaximumExtractedCharacters { get; init; } = 1_000_000;

    /// <summary>
    /// Gets the target size of one chunk, in characters. Roughly 1,500 tokens at four
    /// characters per token, which is small enough to leave room for an answer and large
    /// enough that a page is not split needlessly.
    /// </summary>
    public int ChunkSizeCharacters { get; init; } = 6_000;

    /// <summary>
    /// Gets how much of the previous chunk is repeated at the start of the next one. Without
    /// an overlap, a sentence that straddles a boundary belongs to neither chunk and is lost.
    /// </summary>
    public int ChunkOverlapCharacters { get; init; } = 500;

    /// <summary>
    /// Gets the most chunks that will be sent for one request. A document needing more is
    /// analyzed using this many of its parts, and the answer says so.
    /// </summary>
    public int MaximumChunksPerRequest { get; init; } = 20;

    /// <summary>
    /// Gets the most chunk summaries that will be combined into a final summary. Bounded
    /// separately from the chunk count because combining is the step where a limit that is too
    /// generous turns into one oversized request.
    /// </summary>
    public int MaximumCombinedSummaries { get; init; } = 20;

    /// <summary>
    /// Gets the most characters of document text placed in a single request. This is the limit
    /// that actually keeps a request within what a provider will accept, and it is separate
    /// from <see cref="ChunkSizeCharacters"/> so the two can differ.
    /// </summary>
    public int MaximumCharactersPerRequest { get; init; } = 40_000;

    /// <summary>Gets the most rows read from one worksheet.</summary>
    public int MaximumRowsPerSheet { get; init; } = 500;

    /// <summary>Gets the most columns read from one worksheet.</summary>
    public int MaximumColumnsPerSheet { get; init; } = 50;

    /// <summary>
    /// Gets the most cells read across the whole workbook. A worksheet limit alone is not
    /// enough, because a workbook with two hundred small sheets would pass it.
    /// </summary>
    public int MaximumCells { get; init; } = 20_000;

    /// <summary>
    /// Gets how many chunks are read at once when summarizing a long document. More than a
    /// few at a time buys little and costs a burst of parallel requests.
    /// </summary>
    public int MaximumConcurrentChunkSummaries { get; init; } = 3;
}
