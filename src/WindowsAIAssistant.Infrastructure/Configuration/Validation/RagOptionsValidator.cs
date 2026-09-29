using Microsoft.Extensions.Options;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Configuration.Validation;

/// <summary>
/// Checks that the retrieval knobs describe a judgement somebody could act on.
/// <para>
/// The weights are checked as a pair rather than individually, because a ranking is only a
/// ranking relative to the other half: a lexical weight of 4 with a vector weight of 0.75 is not
/// a strong opinion about wording, it is a broken one. A threshold outside 0 to 1 is refused for
/// the same reason a vector score is defined on that scale — a value of 30 can never be met, and
/// an answer built from a threshold that can never be met is an answer that never appears.
/// </para>
/// </summary>
public sealed class RagOptionsValidator : IValidateOptions<RagOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, RagOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.TopK < 1 || options.TopK > 100)
        {
            failures.Add("Rag:TopK must be between 1 and 100.");
        }

        if (options.MaximumContextCharacters is < 1_000 or > 1_000_000)
        {
            failures.Add("Rag:MaximumContextCharacters must be between 1000 and 1000000.");
        }

        if (options.MinimumSimilarity is < -1d or > 1d)
        {
            failures.Add("Rag:MinimumSimilarity must be between -1 and 1.");
        }

        if (options.SectionTitleWeight is < 0d or > 1d)
        {
            failures.Add("Rag:SectionTitleWeight must be between 0 and 1.");
        }

        if (options.FileNameWeight is < 0d or > 1d)
        {
            failures.Add("Rag:FileNameWeight must be between 0 and 1.");
        }

        if (options.MaximumRedundancyRatio is <= 0d or > 1d)
        {
            failures.Add("Rag:MaximumRedundancyRatio must be greater than 0 and at most 1.");
        }

        if (options.MaximumChunksPerDocument < 1)
        {
            failures.Add("Rag:MaximumChunksPerDocument must be at least 1.");
        }

        // Only meaningful when wording is counted at all. With a lexical weight of zero the
        // vector weight is the whole score, and requiring the pair to sum to one would refuse a
        // deliberate choice to ignore wording.
        if (options.UseHybridSearch
            && Math.Abs(options.VectorWeight + options.LexicalWeight - 1d) > 0.0001d)
        {
            failures.Add("Rag:VectorWeight and Rag:LexicalWeight must add up to 1 between them.");
        }

        if (!options.UseHybridSearch && options.LexicalWeight > 0d && options.VectorWeight <= 0d)
        {
            failures.Add(
                "Rag:VectorWeight must be greater than 0, or the search would rank passages by "
                + "nothing at all.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
