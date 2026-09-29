using Microsoft.Extensions.Options;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Configuration.Validation;

/// <summary>
/// Checks that the knowledge-base limits are values the index can work within.
/// <para>
/// The database file name is checked for a path separator, which is the one that matters: a
/// configuration value of <c>C:\Windows\knowledge.db</c> would let a settings file decide where
/// the application writes, and no other limit here can do anything comparable.
/// </para>
/// </summary>
public sealed class KnowledgeBaseOptionsValidator : IValidateOptions<KnowledgeBaseOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, KnowledgeBaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DatabaseFileName))
        {
            failures.Add("KnowledgeBase:DatabaseFileName must not be empty.");
        }
        else if (options.DatabaseFileName.IndexOfAny(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':']) >= 0)
        {
            failures.Add(
                "KnowledgeBase:DatabaseFileName must be a file name only. The location is chosen "
                + "per user, not by configuration.");
        }

        if (options.MaximumDocuments < 1)
        {
            failures.Add("KnowledgeBase:MaximumDocuments must be at least 1.");
        }

        if (options.MaximumChunksPerDocument < 1)
        {
            failures.Add("KnowledgeBase:MaximumChunksPerDocument must be at least 1.");
        }

        if (options.DefaultRetrievalCount < 1)
        {
            failures.Add("KnowledgeBase:DefaultRetrievalCount must be at least 1.");
        }

        // A maximum below the default would mean the default is never reachable, so a request
        // that asked for nothing in particular could never be satisfied.
        if (options.MaximumRetrievalCount < options.DefaultRetrievalCount)
        {
            failures.Add(
                "KnowledgeBase:MaximumRetrievalCount must be at least DefaultRetrievalCount.");
        }

        if (options.MaximumScannedChunks < 1)
        {
            failures.Add("KnowledgeBase:MaximumScannedChunks must be at least 1.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
