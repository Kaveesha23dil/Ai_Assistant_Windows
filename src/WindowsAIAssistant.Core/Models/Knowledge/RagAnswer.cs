namespace WindowsAIAssistant.Core.Models.Knowledge;

/// <summary>
/// An answer to a question, with the passages it was built from.
/// <para>
/// The sources are part of the answer rather than an extra. An answer from somebody's own
/// documents that does not say which ones is not much use: a person has to be able to open the
/// file, check the passage, and decide whether the model read it correctly. And a source list
/// built from everything that was retrieved, rather than from what was actually sent, would
/// offer them a place to look for an answer that was never derived from it.
/// </para>
/// <para>
/// <see cref="WasAnsweredWithoutSources"/> exists as its own flag because "I found nothing" and
/// "I found something and am not allowed to use it" are different situations that both end with
/// no answer text, and telling them apart in the interface is what lets the sentence a person
/// reads be the right one.
/// </para>
/// </summary>
public sealed record RagAnswer
{
    /// <summary>Gets the answer text, or the sentence explaining why there is none.</summary>
    public required string Text { get; init; }

    /// <summary>Gets the passages the answer was built from, in the order they were used.</summary>
    public IReadOnlyList<KnowledgeSearchResult> Sources { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the search found passages that were close enough to be
    /// worth answering from.
    /// </summary>
    public bool HasMatches { get; init; }

    /// <summary>
    /// Gets a value indicating whether the passages were found by wording alone, with no vector
    /// search at all.
    /// <para>
    /// Carried with the answer because the honest description of how a passage was chosen is part
    /// of the answer. A keyword search is a real fallback when embeddings cannot be produced —
    /// no credential, no network, a permission withdrawn — and calling that a semantic search
    /// would misrepresent it.
    /// </para>
    /// </summary>
    public bool WasLexicalOnly { get; init; }

    /// <summary>Gets a value indicating whether the answer was produced from the passages.</summary>
    public bool IsSuccessful { get; init; }

    /// <summary>Gets the stable code when the question could not be answered.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Gets the sentence to show when the question could not be answered.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets a value indicating whether something was found but nothing was sent anywhere to
    /// produce an answer — because the passages would have had to leave the machine and the
    /// person has not agreed to that.
    /// </summary>
    public bool WasAnsweredWithoutSources { get; init; }

    /// <summary>Gets how many passages were retrieved before the context budget was applied.</summary>
    public int RetrievedCount { get; init; }

    /// <summary>Gets a value indicating whether passages were left out to stay within the budget.</summary>
    public bool WasContextTruncated { get; init; }

    /// <summary>Records an answer built from the passages.</summary>
    public static RagAnswer FromContext(
        string text,
        IReadOnlyList<KnowledgeSearchResult> sources,
        int retrievedCount,
        bool wasTruncated,
        bool lexicalOnly) =>
        new()
        {
            Text = text,
            Sources = sources,
            HasMatches = sources.Count > 0,
            RetrievedCount = retrievedCount,
            WasContextTruncated = wasTruncated,
            WasLexicalOnly = lexicalOnly,
            IsSuccessful = true,
        };

    /// <summary>
    /// Records that the search found nothing close enough to answer from, which is a successful
    /// outcome of a question rather than a failure of it.
    /// </summary>
    public static RagAnswer NoMatches(string sentence) =>
        new()
        {
            Text = sentence,
            IsSuccessful = true,
        };

    /// <summary>Records a failure a person can act on.</summary>
    public static RagAnswer Failure(string errorCode, string message) =>
        new()
        {
            Text = message,
            ErrorCode = errorCode,
            ErrorMessage = message,
            IsSuccessful = false,
        };

    /// <summary>
    /// Records that the passages were found but could not be sent to a provider, so nothing was
    /// generated from them.
    /// </summary>
    public static RagAnswer PermissionDenied(string message, int retrievedCount) =>
        new()
        {
            Text = message,
            ErrorCode = Core.Common.ErrorCodes.KnowledgeAiPermissionDenied,
            ErrorMessage = message,
            HasMatches = true,
            WasAnsweredWithoutSources = true,
            RetrievedCount = retrievedCount,
        };
}

/// <summary>
/// One piece of a streamed answer.
/// <para>
/// Sources arrive as a completed update before any text, because the search finishes before the
/// first word of the answer exists. A caller can therefore show the documents it is about to
/// quote while the answer is still being written, which is the honest order: the person sees
/// where the answer is coming from before they have read it.
/// </para>
/// </summary>
public sealed record RagStreamUpdate
{
    private RagStreamUpdate(RagStreamUpdateKind kind) => Kind = kind;

    /// <summary>Gets what kind of update this is.</summary>
    public RagStreamUpdateKind Kind { get; }

    /// <summary>Gets the text of a partial or complete answer.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Gets the passages the answer is being built from.</summary>
    public IReadOnlyList<KnowledgeSearchResult> Sources { get; init; } = [];

    /// <summary>Gets a value indicating whether the passages were found by wording alone.</summary>
    public bool WasLexicalOnly { get; init; }

    /// <summary>Gets how many passages were retrieved before the context budget was applied.</summary>
    public int RetrievedCount { get; init; }

    /// <summary>Gets the stable code when the question could not be answered.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Gets the sentence to show when the question could not be answered.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Gets the finished answer, on a completed update.</summary>
    public RagAnswer? Answer { get; init; }

    /// <summary>Gets an update announcing that the passages have been found.</summary>
    public static RagStreamUpdate SourcesFound(
        IReadOnlyList<KnowledgeSearchResult> sources,
        int retrievedCount,
        bool lexicalOnly) =>
        new(RagStreamUpdateKind.Sources)
        {
            Sources = sources,
            RetrievedCount = retrievedCount,
            WasLexicalOnly = lexicalOnly,
        };

    /// <summary>Gets an update carrying part of the answer.</summary>
    public static RagStreamUpdate Delta(string text) =>
        new(RagStreamUpdateKind.Delta) { Text = text };

    /// <summary>Gets an update carrying the finished answer.</summary>
    public static RagStreamUpdate Completed(RagAnswer answer) =>
        new(RagStreamUpdateKind.Completed)
        {
            Text = answer.Text,
            Sources = answer.Sources,
            Answer = answer,
        };

    /// <summary>Gets an update reporting a failure.</summary>
    public static RagStreamUpdate Failed(string errorCode, string message, string partialText = "") =>
        new(RagStreamUpdateKind.Failed)
        {
            Text = partialText,
            ErrorCode = errorCode,
            ErrorMessage = message,
        };
}

/// <summary>The kinds of update a streamed answer produces, in the order they arrive.</summary>
public enum RagStreamUpdateKind
{
    /// <summary>The passages have been found, before any answer text exists.</summary>
    Sources,

    /// <summary>A part of the answer.</summary>
    Delta,

    /// <summary>The answer is finished.</summary>
    Completed,

    /// <summary>The question could not be answered. Carries whatever text had already arrived.</summary>
    Failed,
}
