using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Core.Abstractions.Knowledge;

/// <summary>
/// Turns a set of retrieved passages into the labelled context a model is allowed to use.
/// <para>
/// This is the last point where a decision is made about what reaches a provider, so it lives
/// behind an interface: the budget, the ordering, the deduplication, and the rule that a passage
/// is identified by its file name and place rather than by where the file happens to sit on the
/// person's disk are all one behaviour, and they can only be tested if they are one thing.
/// </para>
/// </summary>
public interface IRagContextBuilder
{
    /// <summary>
    /// Builds the context for a question from the passages that were retrieved.
    /// </summary>
    /// <param name="passages">The retrieved passages, already ranked.</param>
    /// <param name="maximumCharacters">The ceiling on the finished context.</param>
    /// <returns>The context and the passages that actually made it into it.</returns>
    RagContext Build(IReadOnlyList<KnowledgeSearchResult> passages, int maximumCharacters);
}

/// <summary>
/// The labelled context for one question, and the passages it was built from.
/// <para>
/// Carrying the used passages alongside the text is what makes a citation honest. A source list
/// built from everything that was retrieved, rather than from what was sent, would offer a
/// person a place to look for an answer that was never derived from it.
/// </para>
/// </summary>
/// <param name="Text">The context, in the form sent to the model.</param>
/// <param name="Sources">The passages that made it in, in the order they appear.</param>
/// <param name="CharacterCount">How long the context is.</param>
/// <param name="WasTruncated">Whether any passage was left out to stay within the budget.</param>
public sealed record RagContext(
    string Text,
    IReadOnlyList<KnowledgeSearchResult> Sources,
    int CharacterCount,
    bool WasTruncated)
{
    /// <summary>Gets a value indicating whether any passage made it into the context.</summary>
    public bool HasSources => Sources.Count > 0;

    /// <summary>Gets a value indicating whether the context has nothing in it.</summary>
    public bool IsEmpty => !HasSources || string.IsNullOrWhiteSpace(Text);
}
