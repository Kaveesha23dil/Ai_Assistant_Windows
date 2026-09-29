using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Knowledge;

/// <summary>
/// Combines a meaning match and a wording match into one ranking, and removes the repetition
/// that chunking produces.
/// <para>
/// The two scores are combined as a weighted sum and the weights are configuration, because
/// how much of the answer should come from each depends on the documents: a base of technical
/// papers wants wording weighted heavily, because the identifiers are the question, and a base of
/// prose wants the opposite. A value that is right for neither is a value nobody should have
/// chosen for them.
/// </para>
/// <para>
/// Three things happen after the arithmetic, and all three are about honesty rather than about
/// score. Near-duplicate passages are dropped, because chunks overlap by design and three copies
/// of one paragraph spend the whole context saying one thing. One document is capped, because a
/// single long document otherwise fills the answer and the person is left with a citation to
/// everything. And the threshold is applied last and is allowed to return fewer than asked for,
/// because a quota filled with passages that merely cleared the bar produces an answer citing
/// something irrelevant — which is worse than an answer saying nothing was found.
/// </para>
/// </summary>
public sealed class HybridSearchRanker : IHybridSearchRanker
{
    private readonly RagRetrievalPolicy _policy;

    public HybridSearchRanker(RagRetrievalPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
    }

    /// <inheritdoc />
    public IReadOnlyList<KnowledgeSearchResult> Rank(
        IReadOnlyList<KnowledgeSearchResult> passages,
        string question,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(passages);

        if (passages.Count == 0 || limit <= 0)
        {
            return [];
        }

        var policy = _policy;
        var questionWords = LexicalSearch.Tokenize(question);

        var scored = new List<(KnowledgeSearchResult Result, double Combined)>(passages.Count);

        foreach (var passage in passages)
        {
            var lexical = passage.LexicalScore > 0d
                ? passage.LexicalScore
                : LexicalSearch.Score(questionWords, passage.Text);

            var combined = Combine(passage, lexical, questionWords, policy);

            scored.Add((passage with { LexicalScore = lexical }, combined));
        }

        var ordered = scored
            .OrderByDescending(entry => entry.Combined)
            .ThenBy(entry => entry.Result.ChunkId)
            .ToArray();

        var kept = ApplyThreshold(ordered, policy);
        kept = RemoveRedundancy(kept, policy);
        kept = ApplyDocumentCap(kept, policy);

        return
        [
            .. kept
                .Take(limit)
                .Select(entry => entry.Result with { CombinedScore = entry.Combined }),
        ];
    }

    /// <summary>
    /// The weighted total, plus what the passage's own labels are worth.
    /// </summary>
    /// <remarks>
    /// A passage with no vector score is not given one. Its total is the wording share of the
    /// non-vector weight, renormalized to the full range, so it competes on equal terms with one
    /// that has both — and so that a keyword find on a degraded path is not permanently ranked
    /// below a vector find that matched nothing, which is what a zero would produce.
    /// </remarks>
    private static double Combine(
        KnowledgeSearchResult passage,
        double lexical,
        IReadOnlyList<string> questionWords,
        RagRetrievalPolicy policy)
    {
        var vector = passage.HasVectorScore ? passage.VectorScore : 0d;

        // The question is tokenized once here rather than inside the loop over a document's
        // sections: a passage can belong to several, and a base of long documents has thousands
        // of them.
        var questionSet = new HashSet<string>(questionWords, StringComparer.Ordinal);

        var sectionBonus = passage.Sections.Any(section =>
            LexicalSearch.Tokenize(section).Any(questionSet.Contains))
                ? policy.SectionTitleWeight
                : 0d;

        var fileNameBonus = questionSet.Count > 0
            && FileNameTokens(passage.FileName).Any(questionSet.Contains)
                ? policy.FileNameWeight
                : 0d;

        if (passage.HasVectorScore)
        {
            var total = (policy.VectorWeight * vector) + (policy.LexicalWeight * lexical);

            // The bonuses are added, not weighted, and are small: they break ties between two
            // passages that are otherwise equally close, and a passage should not outrank a
            // genuinely closer one because its heading happened to name the subject.
            return Math.Clamp(total + sectionBonus + fileNameBonus, 0d, 1d);
        }

        var lexicalOnly = lexical + sectionBonus + fileNameBonus;
        return Math.Clamp(lexicalOnly, 0d, 1d);
    }

    /// <summary>
    /// The words a file name can answer to: the name as tokenized, and the name with its
    /// extension taken off.
    /// <para>
    /// The tokenizer deliberately keeps a dot inside a word so that <c>MAX_RETRY_COUNT</c>,
    /// <c>on-call</c>, and <c>3.11</c> each survive as one token. The cost is that
    /// <c>notice.pdf</c> is a single token that the question word <c>notice</c> can never match,
    /// and every real file has an extension. Without this the file-name weight is configuration
    /// that nothing can ever trigger, and the ranking falls back to a tie-break on a value
    /// nothing controls — which is what made the ordering of two equally-scoring passages depend
    /// on their chunk ids.
    /// </para>
    /// </summary>
    private static IEnumerable<string> FileNameTokens(string? fileName)
    {
        foreach (var token in LexicalSearch.Tokenize(fileName))
        {
            yield return token;

            // "archive.tar.gz" has to answer to "archive" as well as to "tar", and a leading dot
            // is a hidden file rather than an extension, so nothing is offered for it.
            var firstDot = token.IndexOf('.');
            if (firstDot > 0)
            {
                yield return token[..firstDot];
            }

            var lastDot = token.LastIndexOf('.');
            if (lastDot > 0 && lastDot != firstDot)
            {
                yield return token[..lastDot];
            }
        }
    }

    /// <summary>
    /// Drops anything below the bar.
    /// <para>
    /// Applied to the vector score where there is one and to the total otherwise. A passage that
    /// is far away in meaning but mentions the question's words in its heading can otherwise
    /// clear the line on the bonus alone.
    /// </para>
    /// </summary>
    private static List<(KnowledgeSearchResult Result, double Combined)> ApplyThreshold(
        IReadOnlyList<(KnowledgeSearchResult Result, double Combined)> ordered,
        RagRetrievalPolicy policy) =>
    [
        .. ordered.Where(entry =>
            entry.Result.HasVectorScore
                ? entry.Result.VectorScore >= policy.MinimumSimilarity
                : entry.Combined >= LexicalSearch.DefaultMinimumScore),
    ];

    /// <summary>
    /// Removes a passage that says what an earlier one already said.
    /// <para>
    /// Compared on the set of words rather than on the text, because chunks that overlap by
    /// design are not identical — the one after it starts with the sentence the one before it
    /// ended on — and a string comparison would keep all three. The words are compared as a set
    /// so that a repeated paragraph is caught while a passage that quotes the same sentence and
    /// then says something different is not.
    /// </para>
    /// <para>
    /// The comparison is scoped to one document, because the repetition being removed is an
    /// artifact of chunking and chunking happens inside a document. Two files that happen to
    /// share a clause are two sources, and collapsing them would discard the corroboration and
    /// leave a citation list that names one file where the person's base holds two.
    /// </para>
    /// <para>
    /// A passage with no words is dropped here rather than being left to be ranked, because it
    /// cannot be scored and cannot be quoted, and it would still consume a number in the citation
    /// list. That filter applies whether or not there is a second passage to compare against.
    /// </para>
    /// </summary>
    private static List<(KnowledgeSearchResult Result, double Combined)> RemoveRedundancy(
        List<(KnowledgeSearchResult Result, double Combined)> ordered,
        RagRetrievalPolicy policy)
    {
        var kept = new List<(KnowledgeSearchResult Result, double Combined)>(ordered.Count);
        var seenByDocument = new Dictionary<Guid, List<HashSet<string>>>();

        foreach (var entry in ordered)
        {
            var words = new HashSet<string>(LexicalSearch.Tokenize(entry.Result.Text), StringComparer.Ordinal);

            if (words.Count == 0)
            {
                continue;
            }

            if (!seenByDocument.TryGetValue(entry.Result.DocumentId, out var seen))
            {
                seen = [];
                seenByDocument[entry.Result.DocumentId] = seen;
            }

            var redundant = false;

            foreach (var previous in seen)
            {
                if (Overlap(previous, words) >= policy.MaximumRedundancyRatio)
                {
                    redundant = true;
                    break;
                }
            }

            if (redundant)
            {
                continue;
            }

            seen.Add(words);
            kept.Add(entry);
        }

        return kept;
    }

    /// <summary>The share of the smaller set that the larger one also contains.</summary>
    private static double Overlap(HashSet<string> left, HashSet<string> right)
    {
        var shared = 0;
        var smaller = Math.Min(left.Count, right.Count);

        foreach (var word in left)
        {
            if (right.Contains(word))
            {
                shared++;
            }
        }

        return smaller == 0 ? 0d : (double)shared / smaller;
    }

    /// <summary>
    /// Keeps the best passages from each document, up to the configured cap.
    /// <para>
    /// The cap is applied after the ordering, so the passages a document keeps are its best ones
    /// rather than whichever of its passages happened to be scanned first.
    /// </para>
    /// </summary>
    private static List<(KnowledgeSearchResult Result, double Combined)> ApplyDocumentCap(
        List<(KnowledgeSearchResult Result, double Combined)> ordered,
        RagRetrievalPolicy policy)
    {
        if (policy.MaximumChunksPerDocument <= 0)
        {
            return ordered;
        }

        var perDocument = new Dictionary<Guid, int>();
        var kept = new List<(KnowledgeSearchResult Result, double Combined)>(ordered.Count);

        foreach (var entry in ordered)
        {
            var used = perDocument.TryGetValue(entry.Result.DocumentId, out var count) ? count : 0;

            if (used >= policy.MaximumChunksPerDocument)
            {
                continue;
            }

            perDocument[entry.Result.DocumentId] = used + 1;
            kept.Add(entry);
        }

        return kept;
    }
}
