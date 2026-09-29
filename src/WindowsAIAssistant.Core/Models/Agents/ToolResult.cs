using System.Globalization;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// The answer one tool gave, or the reason it could not.
/// <para>
/// A tool never throws for something a person can act on. It returns this instead, carrying
/// both a sentence to show and a stable code to branch on, so the executor can decide whether to
/// stop the plan, carry on to the next step, or ask a question — without parsing English.
/// </para>
/// <para>
/// <see cref="Content"/> and <see cref="Data"/> are separated for a reason that matters more
/// here than anywhere else in the application. <see cref="Content"/> is text taken from a
/// person's documents, a screen, or a model, and it lives only as long as the run. <see cref="Data"/>
/// is metadata — a file name, a count, a format — and it is the only part allowed into the
/// activity timeline or the memory store. A tool that put a passage in <see cref="Data"/> would
/// quietly turn the agent's own persistence into a document archive.
/// </para>
/// </summary>
public sealed record ToolResult
{
    private ToolResult(
        string toolName,
        bool isSuccess,
        string? content,
        IReadOnlyList<string> sources,
        IReadOnlyDictionary<string, string> data,
        string? errorCode,
        string? errorMessage,
        TimeSpan duration,
        IReadOnlyList<AgentAction> actions)
    {
        ToolName = toolName;
        IsSuccess = isSuccess;
        Content = content;
        Sources = sources;
        Data = data;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Duration = duration;
        Actions = actions;
    }

    /// <summary>Gets the tool that produced this result.</summary>
    public string ToolName { get; }

    /// <summary>Gets a value indicating whether the tool did what it was asked to do.</summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the text the tool produced, in whatever form the next step needs it. Not persisted.
    /// </summary>
    public string? Content { get; }

    /// <summary>
    /// Gets the places the content came from, phrased so they can be shown. A knowledge search
    /// that found nothing reports an empty list rather than an invented one, so a reader can
    /// tell a grounded answer from an ungrounded one.
    /// </summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>
    /// Gets the metadata about the run: counts, names, formats. This is the part that may be
    /// written to the activity timeline and the memory store.
    /// </summary>
    public IReadOnlyDictionary<string, string> Data { get; init; }

    /// <summary>Gets the stable code when the tool failed, otherwise <see langword="null"/>.</summary>
    public string? ErrorCode { get; }

    /// <summary>Gets the sentence to show when the tool failed, otherwise <see langword="null"/>.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Gets how long the tool took, recorded so a slow step is visible in the timeline.</summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets the actions the tool actually performed, for the record. A tool that was refused
    /// returns an empty list, because nothing happened.
    /// </summary>
    public IReadOnlyList<AgentAction> Actions { get; }

    /// <summary>Gets a value indicating whether there is text for the next step to work from.</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Content);

    /// <summary>Creates a result carrying the text the next step needs.</summary>
    public static ToolResult Success(
        string toolName,
        string content,
        IEnumerable<string>? sources = null,
        IReadOnlyDictionary<string, string>? data = null,
        TimeSpan duration = default,
        IEnumerable<AgentAction>? actions = null) =>
        new(
            toolName,
            isSuccess: true,
            content,
            sources is null ? [] : [.. sources],
            data ?? EmptyData,
            errorCode: null,
            errorMessage: null,
            duration,
            actions is null ? [] : [.. actions]);

    /// <summary>
    /// Creates a result that did the work but found nothing to report. Distinct from a failure:
    /// a search that matched no document has succeeded, and the executor must not stop the plan
    /// because of it.
    /// </summary>
    public static ToolResult Empty(
        string toolName,
        string? explanation = null,
        IReadOnlyDictionary<string, string>? data = null) =>
        new(
            toolName,
            isSuccess: true,
            explanation,
            [],
            data ?? EmptyData,
            errorCode: null,
            errorMessage: null,
            TimeSpan.Zero,
            []);

    /// <summary>Creates a result reporting a refusal or a fault, with a code and a sentence.</summary>
    public static ToolResult Failure(
        string toolName,
        string errorCode,
        string errorMessage,
        IReadOnlyDictionary<string, string>? data = null) =>
        new(
            toolName,
            isSuccess: false,
            content: null,
            [],
            data ?? EmptyData,
            errorCode,
            errorMessage,
            TimeSpan.Zero,
            []);

    /// <summary>Returns this result with the duration filled in by the executor.</summary>
    public ToolResult WithDuration(TimeSpan duration) =>
        new(
            ToolName,
            IsSuccess,
            Content,
            Sources,
            Data,
            ErrorCode,
            ErrorMessage,
            duration,
            Actions);

    /// <summary>Returns this result with its metadata reduced to what may safely be stored.</summary>
    /// <remarks>
    /// The content and the sources are dropped rather than trimmed. What a step read has no
    /// business in a database the agent keeps between runs, and the one way to guarantee that is
    /// to never hand the persistence layer an object that holds it.
    /// </remarks>
    public ToolResult WithoutContent() =>
        new(
            ToolName,
            IsSuccess,
            content: null,
            [],
            Data,
            ErrorCode,
            ErrorMessage,
            Duration,
            Actions);

    private static IReadOnlyDictionary<string, string> EmptyData { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds metadata from a name and a value, for a tool with a single fact to report.</summary>
    public static IReadOnlyDictionary<string, string> Data1(string name, string value) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [name] = value };

    /// <summary>Builds metadata from a name and a count.</summary>
    public static IReadOnlyDictionary<string, string> Data1(string name, int value) =>
        Data1(name, value.ToString(CultureInfo.InvariantCulture));
}
