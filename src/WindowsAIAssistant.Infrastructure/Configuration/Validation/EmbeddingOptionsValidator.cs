using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Configuration.Validation;

/// <summary>
/// Checks that the embedding numbers are values the provider can actually be called with.
/// <para>
/// Reported rather than corrected, for the document feature's reason: a batch size of zero or a
/// limit of one character would otherwise fail much later, in a request to a paid provider, with
/// an error that says nothing about which setting was wrong.
/// </para>
/// </summary>
public sealed class EmbeddingOptionsValidator : IValidateOptions<EmbeddingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, EmbeddingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        // A provider this build cannot resolve is refused at start-up rather than falling back
        // silently. Falling back would embed a person's documents with a provider they did not
        // choose — which, for the cloud case, is a permissions failure with no error at all.
        if (!EmbeddingProviderKinds.SupportedNames.Contains(options.Provider, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add(
                "Embeddings:Provider must be one of: "
                + $"{string.Join(", ", EmbeddingProviderKinds.SupportedNames)}.");
        }

        if (options.BatchSize is < 1 or > 512)
        {
            failures.Add("Embeddings:BatchSize must be between 1 and 512.");
        }

        if (options.MaximumInputCharacters is < 100 or > 100_000)
        {
            failures.Add("Embeddings:MaximumInputCharacters must be between 100 and 100000.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
