using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Application.Documents;

/// <summary>
/// Ranks a document's chunks by how much a question overlaps their text.
/// <para>
/// This is a word-counting method and nothing more. It is here because it is predictable,
/// fast enough to run on every keystroke of a question, and needs nothing downloaded, indexed,
/// or trained, and because a person asking a question about a document is nearly always
/// asking with words the document uses. What it gives up on is paraphrase: a question about
/// "termination" will not find a passage headed "ending the contract", and no amount of tuning
/// word counts will fix that. That trade is stated plainly in the interface rather than hidden
/// behind a method name implying more than it does.
/// </para>
/// <para>
/// Nothing is embedded and nothing is stored. A rank is discarded as soon as it has been used
/// to order chunks, so the document's text is never written anywhere.
/// </para>
/// </summary>
public sealed partial class DocumentChunkRanker : IDocumentChunkRanker
{
    /// <summary>
    /// Words carrying so little meaning that counting them would rank chunks by filler. Kept
    /// short on purpose: an aggressive list drops the one word that matters, and a single
    /// surviving stopword is cheaper than a missed answer.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "are", "but", "not", "you", "all", "any", "can", "had", "her",
        "was", "one", "our", "out", "day", "get", "has", "him", "his", "how", "its", "may",
        "new", "now", "old", "see", "two", "who", "did", "yes", "she", "with", "that", "this",
        "from", "they", "what", "when", "where", "which", "there", "their", "would", "about",
        "into", "than", "then", "them", "these", "some", "could", "other", "into", "been",
        "does", "each", "more", "most", "must", "only", "over", "such", "very", "will", "your",
    };

    private readonly ILogger<DocumentChunkRanker> _logger;

    public DocumentChunkRanker(ILogger<DocumentChunkRanker> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<RankedDocumentChunk> Rank(
        IReadOnlyList<DocumentChunk> chunks,
        string query,
        int maximumChunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumChunks, 1);

        if (chunks.Count == 0)
        {
            return [];
        }

        var terms = ExtractTerms(query);
        if (terms.Count == 0)
        {
            // Nothing to match on. Handing back the document in its own order is honest: the
            // first page of a document is a better guess than an arbitrary permutation of it.
            _logger.LogInformation(
                "Ranking skipped because the question had no searchable words. Chunks were left in document order.");
            return chunks
                .Take(maximumChunks)
                .Select(chunk => new RankedDocumentChunk(chunk, 0))
                .ToList();
        }

        var ranked = new List<Scored>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var matched = MatchTerms(chunk, terms, out var score);
            ranked.Add(new Scored(new RankedDocumentChunk(chunk, score), matched));
        }

        // A passage that answers more of the question beats one that repeats a single word of it,
        // which is why the number of distinct terms matched is the primary key and the weighted
        // score only breaks a tie. Where two passages are still equal — same breadth, same
        // weight — the longer one is sent: among passages mentioning exactly the same words, the
        // longer is the more likely to contain the sentence that answers, and the shorter is
        // likelier to be a heading or a repeated fragment. Document order settles anything still
        // equal, so the result is the same every time for the same input.
        var ordered = ranked
            .OrderByDescending(entry => entry.MatchedTerms)
            .ThenByDescending(entry => entry.Ranked.Score)
            .ThenByDescending(entry => entry.Ranked.Chunk.CharacterCount)
            .ThenBy(entry => entry.Ranked.Chunk.Sequence)
            .Take(maximumChunks)
            .Select(entry => entry.Ranked)
            .ToList();

        _logger.LogInformation(
            "Ranked {ChunkCount} chunks against the question and kept the top {SelectedCount}.",
            chunks.Count,
            ordered.Count);

        return ordered;
    }

    /// <summary>
    /// Endings dropped when a word is added to the search set, longest first.
    /// <para>
    /// A person asking "when does it renew" is asking about a heading that says "Renewal date",
    /// and a word-counting ranker that cannot see that will return the wrong passage with total
    /// confidence. Reducing both to a shared root covers the common English cases for a few lines
    /// of code. It is still a heuristic, not a stemmer: it will not connect "terminate" to
    /// "ending", and the type's documentation says so rather than pretending otherwise.
    /// </para>
    /// </summary>
    private static readonly string[] DerivationalSuffixes = ["ing", "ment", "ance", "ence", "tion", "sion", "al", "ed", "es", "s"];

    /// <summary>
    /// What a term is worth when it appears in a passage's body.
    /// </summary>
    private const int BodyTermWeight = 1;

    /// <summary>
    /// What a term is worth when it appears in the name of the section the passage came from.
    /// A heading is a statement about what follows it rather than one sentence among many, so the
    /// same word means more there than it does partway down a paragraph.
    /// </summary>
    private const int SectionNameTermWeight = 3;

    /// <summary>
    /// Reduces a question to the distinct words worth searching for.
    /// </summary>
    internal static HashSet<string> ExtractTerms(string query)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(query))
        {
            return terms;
        }

        foreach (Match match in WordPattern().Matches(query.ToLowerInvariant()))
        {
            var word = match.Value;

            if (word.Length < 3 || StopWords.Contains(word))
            {
                continue;
            }

            // A word in more than one form is one word as far as a reader is concerned, so a
            // question about "employees" reaches a passage about "employee" and a question about
            // "renew" reaches one about "renewal".
            AddWithRoots(terms, word);
        }

        return terms;
    }

    private static void AddWithRoots(HashSet<string> terms, string word)
    {
        foreach (var form in RootForms(word))
        {
            terms.Add(form);
        }
    }

    private static int MatchTerms(DocumentChunk chunk, HashSet<string> terms, out int score)
    {
        score = 0;
        var found = new HashSet<string>(terms.Count, StringComparer.OrdinalIgnoreCase);

        foreach (Match match in WordPattern().Matches(chunk.Text.ToLowerInvariant()))
        {
            // A word counts once, however many times the passage says it, and it is reduced the
            // same way the question's words were: a passage headed "Renewal date" is what someone
            // asking when it renews is looking for, and only a shared root connects the two.
            foreach (var form in RootForms(match.Value))
            {
                if (terms.Contains(form))
                {
                    found.Add(form);
                    break;
                }
            }
        }

        score = BodyTermWeight * found.Count;

        // A term in a section's name is worth more than the same term in its body, because a
        // heading is a statement about what follows it rather than one of many sentences.
        foreach (var section in chunk.Sections)
        {
            foreach (Match match in WordPattern().Matches(section.ToLowerInvariant()))
            {
                foreach (var form in RootForms(match.Value))
                {
                    if (terms.Contains(form))
                    {
                        score += SectionNameTermWeight;
                        break;
                    }
                }
            }
        }

        return found.Count;
    }

    /// <summary>
    /// A word together with the shorter words it would be written as: the endings dropped when
    /// one is added to the other.
    /// </summary>
    private static IEnumerable<string> RootForms(string word)
    {
        yield return word;

        foreach (var suffix in DerivationalSuffixes)
        {
            if (word.Length - suffix.Length < 4)
            {
                // Too short to be the same word after removing anything. Guards against "gas"
                // becoming "ga" and matching every passage that mentions a "ga" somewhere.
                continue;
            }

            if (word.EndsWith(suffix, StringComparison.Ordinal))
            {
                yield return word[..^suffix.Length];
            }
        }
    }

    [GeneratedRegex(@"[a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    /// <summary>
    /// A ranked chunk and the count of distinct query words it contains. The count is kept apart
    /// from <see cref="RankedDocumentChunk"/> because it is a fact about how this ranking was
    /// done rather than a property of the chunk, and putting it on the shared model would make
    /// it available everywhere it could be mistaken for part of the document.
    /// </summary>
    private readonly record struct Scored(RankedDocumentChunk Ranked, int MatchedTerms);
}
