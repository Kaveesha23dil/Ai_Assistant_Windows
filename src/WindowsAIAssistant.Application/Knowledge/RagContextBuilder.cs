using System.Text;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Knowledge;

/// <summary>
/// Turns retrieved passages into the labelled context a model is allowed to answer from.
/// <para>
/// Every passage is labelled with the document it came from and, when the document named one, the
/// place inside it. The labelling is what makes a citation checkable: a model that says
/// "contracts may be terminated with 30 days' notice [Contract v3 — clause 4.2]" has told the
/// reader where to look, and one that says "[source 2]" has not.
/// </para>
/// <para>
/// Passages are dropped rather than shortened once the budget is spent. A passage cut in the
/// middle is a passage whose end has been invented, and an answer built from half a clause is
/// exactly the failure this feature is meant to avoid. So every passage is tried, and the first
/// that does not fit is remembered rather than acted on — a shorter passage further down the
/// ranking will often fit where the longest one did not, and dropping the first would have thrown
/// it away without asking.
/// </para>
/// <para>
/// The one exception is when nothing at all fitted. Then the remembered passage is cut, because
/// an empty context in reply to a question with a relevant passage in it is the worse failure.
/// The cut is marked, and the marker is charged against the budget, so a marked context is still
/// within the limit it was measured against.
/// </para>
/// <para>
/// The budget covers the labels as well as the text, and the separators are charged before the
/// passage is measured. An undercount by the width of a label per passage would let the context
/// exceed the model's window by a few hundred characters on exactly the day a person's context
/// fills up.
/// </para>
/// </summary>
public sealed class RagContextBuilder : IRagContextBuilder
{
    /// <summary>What goes between one passage and the next.</summary>
    private const string Separator = "\n\n---\n\n";

    /// <summary>
    /// The newline that follows a label, and the brackets around it. Charged separately so the
    /// budget and the marker can be reasoned about without re-deriving it from a format string.
    /// </summary>
    private const int LabelOverhead = 3;

    /// <summary>
    /// What is appended to a passage that had to be cut. Newline included, so the length here is
    /// exactly what the budget pays for it.
    /// </summary>
    private const string TruncationMarker = "\n[truncated]";

    /// <inheritdoc />
    public RagContext Build(IReadOnlyList<KnowledgeSearchResult> passages, int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(passages);

        if (passages.Count == 0 || maximumCharacters <= 0)
        {
            return new RagContext(string.Empty, [], 0, false);
        }

        var builder = new StringBuilder();
        var used = new List<KnowledgeSearchResult>(passages.Count);
        var truncated = false;

        // The first passage that would not fit on its own, held rather than cut on the spot.
        // Cutting immediately meant a long passage at the top of the ranking ended the whole
        // build, so a shorter passage underneath it that would have fitted perfectly was never
        // reached — the context came back containing half a sentence from the least useful of the
        // two. Holding it and cutting only once nothing at all has fitted gets the better of both.
        KnowledgeSearchResult? oversized = null;

        foreach (var passage in passages)
        {
            if (string.IsNullOrWhiteSpace(passage.Text))
            {
                continue;
            }

            var label = FormatLabel(passage, used.Count + 1);
            var block = $"[{label}]\n{passage.Text.Trim()}";
            var cost = (builder.Length > 0 ? Separator.Length : 0) + block.Length;

            if (builder.Length + cost > maximumCharacters)
            {
                // A passage that will not fit is left out and the next one is tried, because the
                // ranking is not the same as the order: a shorter passage ranked lower can be
                // worth more than the longest one ranked higher.
                truncated = true;
                oversized ??= passage;
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(Separator);
            }

            builder.Append(block);
            used.Add(passage);
        }

        if (used.Count == 0 && oversized is not null)
        {
            // Nothing fitted, and there is a passage that would have fitted if it had been shorter.
            // It is cut, because returning an empty context to a question with a relevant passage
            // in it reports "your documents do not mention this" about a document that does.
            //
            // The marker is charged against the budget before the passage is measured. It used to
            // be appended afterwards, which made the finished context longer than the limit it
            // had just been measured against — on this path, the only path, every context was
            // over budget and the overrun was exactly the marker's width.
            var label = FormatLabel(oversized, 1);
            var text = oversized.Text.Trim();
            var room = Math.Max(0, maximumCharacters - label.Length - LabelOverhead - TruncationMarker.Length);

            builder.Append('[')
                .Append(label)
                .Append(']')
                .Append('\n')
                .Append(text[..Math.Min(text.Length, room)])
                .Append(TruncationMarker);

            used.Add(oversized);

            return new RagContext(builder.ToString(), used, builder.Length, WasTruncated: true);
        }

        return new RagContext(builder.ToString(), used, builder.Length, truncated);
    }

    /// <summary>
    /// The label a passage is cited under.
    /// <para>
    /// A number first, so a model can refer to "source 1" unambiguously, then the file's name and
    /// then whatever place the document itself reported. The file's path is never included: where
    /// a document sits on somebody's disk is information no model needs and no citation is
    /// improved by, and it is one of the few things here that would be a privacy cost rather than
    /// a benefit.
    /// </para>
    /// </summary>
    private static string FormatLabel(KnowledgeSearchResult passage, int ordinal)
    {
        var label = new StringBuilder();

        label.Append("Source ").Append(ordinal);

        if (!string.IsNullOrWhiteSpace(passage.FileName))
        {
            label.Append(": ").Append(passage.FileName.Trim());
        }

        if (passage.SourceReference.HasAny)
        {
            label.Append(" \u2014 ").Append(passage.SourceReference.Display);
        }

        return label.ToString();
    }
}
