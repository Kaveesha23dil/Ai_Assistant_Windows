using System.Text;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Knowledge;

/// <summary>
/// Scores how much of a question's wording a passage shares, and finds passages by wording alone.
/// <para>
/// This is the half of retrieval that needs no model, no network, and no permission. It is here
/// because both halves are needed even when the other is unavailable: an exact identifier, a
/// person's name, a file name, a version number — the things a vector is worst at are precisely
/// the things people ask about most, and a search that cannot find <c>JWT_REFRESH_TOKEN</c>
/// because a model averaged it away is not a search worth having.
/// </para>
/// <para>
/// Two different jobs, one set of parts. The tokenizer and the scorer are shared so that a
/// keyword find and a keyword boost cannot disagree about what a word is — if they did, a passage
/// could score highly on one and find nothing on the other, and the fallback would silently
/// return less than the ranking promised.
/// </para>
/// </summary>
public static class LexicalSearch
{
    /// <summary>
    /// The words that appear in nearly every passage and so distinguish nothing.
    /// <para>
    /// A question, unlike a passage, is short: "what is the retry policy for the payment
    /// service" is four content words out of eight. Left in, "what", "is", "for", and "the"
    /// would match almost every passage in a base and the score would be near-identical
    /// everywhere, which is the same as scoring nothing.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "about", "an", "and", "are", "as", "at", "be", "been", "but", "by", "can", "could",
        "did", "do", "does", "for", "from", "had", "has", "have", "how", "i", "if", "in", "into",
        "is", "it", "its", "me", "my", "of", "on", "or", "our", "out", "over", "should", "so",
        "some", "such", "than", "that", "the", "their", "them", "then", "there", "these", "they",
        "this", "to", "was", "we", "were", "what", "when", "where", "which", "who", "why", "will",
        "with", "would", "you", "your",
    };

    /// <summary>
    /// Splits text into lower-case words, dropping the ones that match everything.
    /// <para>
    /// Words of one letter are kept, unlike the embedding provider's tokenizer: in a question
    /// about programming "c" or "r" is often the entire subject, and a stop-word list of common
    /// English words has no business removing it.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var words = new List<string>();
        var builder = new StringBuilder();

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '-' or '.')
            {
                // Underscores, hyphens, and dots stay inside a word so that
                // MAX_RETRY_COUNT, on-call, and 3.11 are each one token rather than three.
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (builder.Length > 0)
            {
                Add(builder.ToString(), words);
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            Add(builder.ToString(), words);
        }

        return words;
    }

    private static void Add(string word, List<string> words)
    {
        if (StopWords.Contains(word))
        {
            return;
        }

        words.Add(word);
    }

    /// <summary>
    /// Scores one passage against a question, from 1 down to 0.
    /// </summary>
    /// <remarks>
    /// The share of the question's content words the passage contains, with two adjustments that
    /// both exist because a plain share is a bad ranker on real documents.
    /// <para>
    /// A rare word counts for more than a common one, because a question about "authentication"
    /// matching a passage that also says "authentication" once is meaningful, while every
    /// passage in a base saying "document" is not. The weight is inverse frequency over the
    /// question's own words, which is cheap and needs no statistics about the corpus — a corpus
    /// statistic would go stale as documents are added and would have to be stored.
    /// </para>
    /// <para>
    /// A passage containing a shorter span of consecutive question words is worth more than one
    /// containing the same words scattered across it, because "retry policy" appearing together
    /// means the passage is about it and the same two words a page apart means nothing. The bonus
    /// is bounded, so it can reorder close results without overriding a genuine semantic match.
    /// </para>
    /// </remarks>
    public static double Score(IReadOnlyList<string> questionWords, string? passageText)
    {
        if (questionWords.Count == 0 || string.IsNullOrWhiteSpace(passageText))
        {
            return 0d;
        }

        var passageWords = Tokenize(passageText);
        if (passageWords.Count == 0)
        {
            return 0d;
        }

        var present = new HashSet<string>(passageWords, StringComparer.Ordinal);
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        var totalWeight = 0d;
        var matchedWeight = 0d;

        foreach (var word in questionWords)
        {
            // A word repeated in the question is not weighted twice. "what is the retry policy
            // for the payment service" says "the" twice, and "payment" once: the weight is
            // inverse frequency, so the word the person actually typed once carries the most.
            var frequency = questionWords.Count(candidate => string.Equals(candidate, word, StringComparison.Ordinal));
            var weight = 1d / Math.Sqrt(frequency);

            weights[word] = weight;
            totalWeight += weight;

            if (present.Contains(word))
            {
                matchedWeight += weight;
            }
        }

        if (totalWeight <= 0d)
        {
            return 0d;
        }

        var score = matchedWeight / totalWeight;

        var longestRun = LongestConsecutiveRun(questionWords, present);
        if (longestRun >= 2)
        {
            score += Math.Min(0.2d, 0.05d * longestRun);
        }

        return Math.Clamp(score, 0d, 1d);
    }

    /// <summary>The longest run of the question's words appearing one after another in the passage.</summary>
    private static int LongestConsecutiveRun(IReadOnlyList<string> questionWords, HashSet<string> present)
    {
        var longest = 0;
        var current = 0;
        var previousIndex = -2;

        for (var index = 0; index < questionWords.Count; index++)
        {
            if (!present.Contains(questionWords[index]))
            {
                current = 0;
                previousIndex = -2;
                continue;
            }

            // Only a word appearing immediately after the previous question word counts as a
            // run. "retry" then "policy" in the question, and both present in the passage but a
            // page apart, are two matches and not a phrase.
            current = index == previousIndex + 1 ? current + 1 : 1;
            previousIndex = index;

            if (current > longest)
            {
                longest = current;
            }
        }

        return longest;
    }

    /// <summary>
    /// Finds the best passages by wording alone, for when no vector can be produced.
    /// </summary>
    /// <param name="question">The question, in the person's own words.</param>
    /// <param name="candidates">
    /// The passages to score. They are already filtered by base and by any document filter, so
    /// nothing here re-derives it: a fallback that quietly widened the filter would answer a
    /// narrowed question with a document the person excluded.
    /// </param>
    /// <param name="limit">The most passages to return.</param>
    /// <param name="minimumScore">The lowest score worth returning.</param>
    /// <returns>The passages, best first, each with its <see cref="KnowledgeSearchResult.LexicalScore"/> filled in.</returns>
    public static IReadOnlyList<KnowledgeSearchResult> Find(
        string question,
        IReadOnlyList<KnowledgeSearchResult> candidates,
        int limit,
        double minimumScore)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var questionWords = Tokenize(question);
        if (questionWords.Count == 0 || limit <= 0)
        {
            return [];
        }

        var scored = new List<KnowledgeSearchResult>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var score = Score(questionWords, candidate.Text);

            if (score >= minimumScore)
            {
                scored.Add(candidate with { LexicalScore = score });
            }
        }

        return
        [
            .. scored
                .OrderByDescending(result => result.LexicalScore)
                .ThenBy(result => result.ChunkId)
                .Take(limit),
        ];
    }

    /// <summary>The floor a keyword-only search uses when nothing better is available.</summary>
    /// <remarks>
    /// Low, because a keyword search is a fallback and the caller has already been told the
    /// answer comes from wording alone. Set high it would refuse most questions outright on the
    /// degraded path, which is the opposite of graceful.
    /// </remarks>
    public const double DefaultMinimumScore = 0.15;
}
