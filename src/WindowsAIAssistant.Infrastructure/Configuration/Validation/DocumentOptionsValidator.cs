using Microsoft.Extensions.Options;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Configuration.Validation;

/// <summary>
/// Checks that the document limits are values the reader can actually work with.
/// <para>
/// These are reported as failures rather than quietly corrected. A chunk size that has been
/// rounded into something workable would hide the fact that the configured value was
/// nonsense, and the first sign of that would be an answer that mysteriously covered less of
/// the document than expected.
/// </para>
/// </summary>
public sealed class DocumentOptionsValidator : IValidateOptions<DocumentOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DocumentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.MaximumFileSizeMb is < 1 or > 4_096)
        {
            failures.Add("Documents:MaximumFileSizeMb must be between 1 and 4096.");
        }

        if (options.MaximumExtractedCharacters < 1_000)
        {
            failures.Add("Documents:MaximumExtractedCharacters must be at least 1000.");
        }

        if (options.ChunkSizeCharacters < 500)
        {
            failures.Add("Documents:ChunkSizeCharacters must be at least 500.");
        }

        // An overlap as large as the chunk would mean every chunk starts where the previous
        // one ended, so the document would barely advance and the loop would never finish.
        if (options.ChunkOverlapCharacters < 0 ||
            options.ChunkOverlapCharacters >= options.ChunkSizeCharacters)
        {
            failures.Add(
                "Documents:ChunkOverlapCharacters must be at least 0 and smaller than ChunkSizeCharacters.");
        }

        if (options.MaximumChunksPerRequest < 1)
        {
            failures.Add("Documents:MaximumChunksPerRequest must be at least 1.");
        }

        if (options.MaximumCombinedSummaries < 1)
        {
            failures.Add("Documents:MaximumCombinedSummaries must be at least 1.");
        }

        if (options.MaximumCharactersPerRequest < 1_000)
        {
            failures.Add("Documents:MaximumCharactersPerRequest must be at least 1000.");
        }

        if (options.MaximumRowsPerSheet < 1)
        {
            failures.Add("Documents:MaximumRowsPerSheet must be at least 1.");
        }

        if (options.MaximumColumnsPerSheet < 1)
        {
            failures.Add("Documents:MaximumColumnsPerSheet must be at least 1.");
        }

        if (options.MaximumCells < 1)
        {
            failures.Add("Documents:MaximumCells must be at least 1.");
        }

        if (options.MaximumConcurrentChunkSummaries < 1 || options.MaximumConcurrentChunkSummaries > 16)
        {
            failures.Add("Documents:MaximumConcurrentChunkSummaries must be between 1 and 16.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
