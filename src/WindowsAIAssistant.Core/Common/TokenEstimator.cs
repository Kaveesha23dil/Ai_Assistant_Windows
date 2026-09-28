namespace WindowsAIAssistant.Core.Common;

/// <summary>
/// A rough conversion from text to the units an AI provider bills and limits requests in.
/// <para>
/// This is an approximation and is treated as one. Counting exact tokens means embedding a
/// provider's tokenizer, which would tie chunking to whichever provider happens to be
/// configured and would mean a different chunk size for every model. The estimate is used
/// only to keep requests inside a size the provider will accept, where being slightly out
/// either way is harmless.
/// </para>
/// </summary>
public static class TokenEstimator
{
    /// <summary>
    /// The average number of characters a token covers. Four is the rule of thumb for English
    /// text; code and non-Latin scripts differ, which is exactly why this is an estimate and
    /// not a measurement.
    /// </summary>
    public const double CharactersPerToken = 4.0;

    /// <summary>Estimates how many tokens a piece of text is worth.</summary>
    public static int EstimateTokens(string? text) =>
        string.IsNullOrEmpty(text)
            ? 0
            : (int)Math.Ceiling(text.Length / CharactersPerToken);
}
