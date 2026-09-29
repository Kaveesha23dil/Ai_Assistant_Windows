using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Embeddings;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Embeddings;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Embeddings;

/// <summary>
/// Produces vectors on this machine, with no model and no network, by hashing the words of the
/// text into a fixed-width vector.
/// <para>
/// This is not an embedding model and does not pretend to be one. It has no notion of meaning, so
/// "log-in" and "authentication" are unrelated to it, and no amount of tuning will change that.
/// What it does have is the three properties the rest of the feature needs to exist and be
/// testable: it is instant, it is free, and it is completely deterministic — the same text always
/// produces the same vector, in this build and the next, on any machine.
/// </para>
/// <para>
/// That determinism is why it is here rather than behind a flag. It is what lets a search, a
/// reindex, a model-change check, and a hundred assertions about ranking all be tested with no
/// account, no network, and no cost, and it is what lets the application work at all on a machine
/// with neither. It is also the honest answer to "what happens when a person has not configured a
/// provider": a worse search, clearly labelled, rather than a feature that does nothing.
/// </para>
/// <para>
/// The construction is a signed hashing scheme over unigrams and adjacent word pairs. Pairs are
/// included because word order carries real meaning — "the cat sat on the mat" and "the mat sat
/// on the cat" share every word — and the two of them must not produce the same vector. Each
/// component accumulates a fixed pseudo-random value derived from the hash, which keeps unrelated
/// words near enough to uncorrelated to be usable as a bag of hashed features.
/// </para>
/// </summary>
public sealed class MockEmbeddingProvider : IEmbeddingProvider
{
    /// <summary>
    /// The width of the vectors this provider produces. 256 rather than 1,536 because nothing
    /// downstream benefits from a wider vector here, and a knowledge base of a few thousand
    /// passages stays small at a kilobyte each.
    /// </summary>
    public const int Dimensions = 256;

    private const string ProviderName = "Mock";
    private const string DefaultModelName = "mock-hash-256";

    private readonly IOptionsMonitor<EmbeddingOptions> _options;

    public MockEmbeddingProvider(IOptionsMonitor<EmbeddingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public EmbeddingProviderKind ProviderKind => EmbeddingProviderKind.Mock;

    /// <inheritdoc />
    public string? DefaultModel => DefaultModelName;

    /// <inheritdoc />
    public bool IsCloudHosted => false;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public int? KnownDimensions => Dimensions;

    /// <inheritdoc />
    public Task<EmbeddingVector> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Embed(text, Options.NormalizeVectors));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmbeddingVector>> GenerateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var normalize = Options.NormalizeVectors;
        var results = new List<EmbeddingVector>(texts.Count);

        for (var index = 0; index < texts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(Embed(texts[index], normalize));
        }

        return Task.FromResult<IReadOnlyList<EmbeddingVector>>(results);
    }

    private EmbeddingOptions Options => _options.CurrentValue;

    /// <summary>
    /// Hashes the text into a vector.
    /// </summary>
    /// <remarks>
    /// The one hash used is SHA-256, taken over the UTF-8 bytes, and only its first four bytes
    /// become the component index and sign. SHA-256 rather than <see cref="string.GetHashCode"/>
    /// because that one is deliberately randomized per process on .NET: it would make every
    /// vector in one run disagree with every vector in the next, and the whole index would be
    /// invalidated by restarting the application.
    /// </remarks>
    private static EmbeddingVector Embed(string? text, bool normalize)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            // An all-zero vector has no direction, so it is returned unscaled and IsNormalized
            // reports false rather than the values being divided by a magnitude of nothing.
            return new EmbeddingVector(
                new float[Dimensions],
                DefaultModelName,
                ProviderName,
                normalize: false);
        }

        var values = new float[Dimensions];
        var words = Tokenize(text);
        var model = DefaultModelName;

        foreach (var word in words)
        {
            Accumulate(values, model, word);
        }

        // Adjacent pairs, so two sentences made of the same words are not the same sentence.
        for (var index = 0; index + 1 < words.Length; index++)
        {
            Accumulate(values, model, words[index] + '\u0001' + words[index + 1]);
        }

        return new EmbeddingVector(values, model, ProviderName, normalize);
    }

    private static void Accumulate(float[] values, string model, string token)
    {
        Span<byte> digest = stackalloc byte[32];
        var written = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(model + "\u0001" + token),
            digest);

        var index = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(digest) % (uint)values.Length;
        var sign = (digest[written - 1] & 1) == 0 ? 1f : -1f;

        values[index] += sign;
    }

    /// <summary>
    /// Splits the text into lower-case words, keeping the ones that carry meaning.
    /// </summary>
    /// <remarks>
    /// The stop words are dropped because there are enough of them, and enough documents, that
    /// they would otherwise dominate the vector: "the", "of", and "and" appear in nearly every
    /// passage, so a vector built from them is close to the same for all of them and stops
    /// distinguishing anything. Numbers and single letters are kept, because in a technical
    /// document "3" and "C" are often the point.
    /// </remarks>
    private static string[] Tokenize(string text)
    {
        var builder = new StringBuilder();
        var tokens = new List<string>();
        var normalized = text.ToLowerInvariant();

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                continue;
            }

            if (builder.Length > 0)
            {
                Add(builder.ToString(), tokens);
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            Add(builder.ToString(), tokens);
        }

        return tokens.Count == 0 ? [""] : [.. tokens];
    }

    private static void Add(string token, List<string> tokens)
    {
        if (token.Length < 2 && !char.IsDigit(token[0]))
        {
            return;
        }

        if (StopWords.Contains(token))
        {
            return;
        }

        tokens.Add(token);
    }

    /// <summary>
    /// The words that appear in almost every passage and so distinguish nothing. Short and
    /// fixed on purpose: a larger list would start removing words that matter in a document about
    /// language, and this provider is a fallback, not a linguistics project.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "that", "with", "this", "from", "have", "has", "had", "was", "were",
        "are", "but", "not", "you", "your", "they", "their", "them", "its", "it", "in", "on", "at",
        "to", "of", "or", "an", "be", "is", "as", "by", "we", "he", "she", "do", "does", "did",
        "will", "would", "can", "could", "should", "there", "these", "those", "if", "then", "than",
        "so", "such", "which", "who", "whom", "been", "being", "into", "out", "up", "down", "over",
    };

    /// <summary>
    /// The space this provider's vectors live in, needed by the indexer before its first vector
    /// exists.
    /// </summary>
    public static EmbeddingSpace Space => new(ProviderName, DefaultModelName, Dimensions);

    /// <summary>
    /// Reports the width as a string, for the option validation message.
    /// </summary>
    internal static string DimensionsText => Dimensions.ToString(CultureInfo.InvariantCulture);
}
