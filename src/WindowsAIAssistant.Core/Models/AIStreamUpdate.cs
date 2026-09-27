using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models;

/// <summary>
/// A single step of a streamed answer, independent of the provider that produced it.
/// <para>
/// One type covers the whole stream rather than a hierarchy, because every consumer wants the
/// same three things: the kind, the text to show so far, and enough outcome detail to report a
/// stop that was not a completion. <see cref="Kind"/> says which of those is meaningful, so a
/// caller cannot accidentally treat a failure as a finished answer.
/// </para>
/// </summary>
public sealed record AIStreamUpdate
{
    private AIStreamUpdate(
        AIStreamUpdateKind kind,
        string text,
        AIResponse? response,
        string? errorCode,
        string? errorMessage)
    {
        Kind = kind;
        Text = text;
        Response = response;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Gets what this update reports.</summary>
    public AIStreamUpdateKind Kind { get; }

    /// <summary>
    /// Gets the text for this update. A <see cref="AIStreamUpdateKind.Delta"/> carries the new
    /// fragment; every other kind carries everything received so far, so a caller that only
    /// wants the latest full text can read this one property throughout.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the finished response, set on <see cref="AIStreamUpdateKind.Started"/> for the
    /// provider and model, and on <see cref="AIStreamUpdateKind.Completed"/> for the answer.
    /// </summary>
    public AIResponse? Response { get; }

    /// <summary>
    /// Gets the stable, user-safe failure code on a
    /// <see cref="AIStreamUpdateKind.Failed"/> update; otherwise <see langword="null"/>.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>Gets the user-safe message on a failure update; otherwise <see langword="null"/>.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Creates the update that marks the request as accepted.</summary>
    public static AIStreamUpdate Started(AIProviderType provider, string? model) =>
        new(
            AIStreamUpdateKind.Started,
            string.Empty,
            AIResponse.Success(string.Empty, provider, model),
            null,
            null);

    /// <summary>Creates an update carrying a new fragment of the answer.</summary>
    public static AIStreamUpdate Delta(string text) => new(AIStreamUpdateKind.Delta, text, null, null, null);

    /// <summary>Creates the update that carries a completed answer.</summary>
    public static AIStreamUpdate Completed(AIResponse response, string text) =>
        new(AIStreamUpdateKind.Completed, text, response, null, null);

    /// <summary>
    /// Creates the update that reports an early stop, keeping the text that had already
    /// arrived so a partial answer is not thrown away.
    /// </summary>
    public static AIStreamUpdate Failed(
        string text,
        AIResponse response,
        string errorCode,
        string? errorMessage) =>
        new(AIStreamUpdateKind.Failed, text, response, errorCode, errorMessage);
}
