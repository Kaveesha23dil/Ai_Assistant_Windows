using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Abstractions.Time;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Knowledge.Commands.ManageKnowledgeBase;

/// <summary>
/// The outcome of a base being created, renamed, or removed.
/// <para>
/// Carries the base where there is one and a stable error code where there is not, because the
/// page has to be able to say "that name is already taken" rather than throw, and because the
/// codes are what the voice path speaks from.
/// </para>
/// </summary>
public sealed record KnowledgeBaseResult
{
    public required bool IsSuccess { get; init; }

    public KnowledgeBase? Base { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static KnowledgeBaseResult Created(KnowledgeBase knowledgeBase) =>
        new() { IsSuccess = true, Base = knowledgeBase };

    public static KnowledgeBaseResult Updated(KnowledgeBase knowledgeBase) =>
        new() { IsSuccess = true, Base = knowledgeBase };

    public static KnowledgeBaseResult Removed() => new() { IsSuccess = true };

    public static KnowledgeBaseResult Failure(string errorCode, string message) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = message };
}

/// <summary>Creates, renames, and removes knowledge bases.</summary>
public sealed class ManageKnowledgeBaseHandler
{
    /// <summary>
    /// The longest name a base may have.
    /// <para>
    /// Checked here rather than only in the store, so the page gets a refusal it can show
    /// instead of an exception, and so a name is refused the same way whether it came from the
    /// page or from the voice path.
    /// </para>
    /// </summary>
    private const int MaxNameLength = 100;

    private readonly IKnowledgeBaseRepository _bases;
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly KnowledgeProcessingLimits _limits;
    private readonly IDateTimeProvider _clock;

    public ManageKnowledgeBaseHandler(
        IKnowledgeBaseRepository bases,
        IKnowledgeDocumentRepository documents,
        KnowledgeProcessingLimits limits,
        IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(bases);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(clock);

        _bases = bases;
        _documents = documents;
        _limits = limits;
        _clock = clock;
    }

    /// <summary>
    /// Creates a base, or returns the default one when no name is given.
    /// <para>
    /// The default is created on demand rather than at start-up, so a person who never uses the
    /// feature does not accumulate a base they never asked for, and a person who deletes it does
    /// not find it back on the next launch.
    /// </para>
    /// </summary>
    public async Task<KnowledgeBaseResult> CreateAsync(
        string? name = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_limits.Enabled)
        {
            return KnowledgeBaseResult.Failure(
                ErrorCodes.KnowledgeOperationFailed,
                "The knowledge base is turned off.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            var existing = await _bases.GetOrCreateDefaultAsync(cancellationToken).ConfigureAwait(false);
            return KnowledgeBaseResult.Created(existing);
        }

        var trimmed = name.Trim();

        if (trimmed.Length > MaxNameLength)
        {
            return KnowledgeBaseResult.Failure(
                ErrorCodes.KnowledgeBaseNameTaken,
                $"A knowledge base name can be at most {MaxNameLength} characters.");
        }

        var bases = await _bases.ListAsync(cancellationToken).ConfigureAwait(false);

        // Compared without regard to case, because two bases called "Notes" and "notes" are the
        // same base to a person reading a list of them, and finding neither one later is a
        // worse outcome than refusing the second name.
        if (bases.Any(candidate => string.Equals(candidate.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return KnowledgeBaseResult.Failure(
                ErrorCodes.KnowledgeBaseNameTaken,
                $"A knowledge base called \"{trimmed}\" already exists.");
        }

        try
        {
            var created = await _bases
                .AddAsync(KnowledgeBase.Create(trimmed, _clock.UtcNow, description), cancellationToken)
                .ConfigureAwait(false);

            return KnowledgeBaseResult.Created(created);
        }
        catch (KnowledgeException exception)
        {
            return KnowledgeBaseResult.Failure(exception.ErrorCode, exception.Message);
        }
    }

    public async Task<KnowledgeBaseResult> RenameAsync(
        Guid knowledgeBaseId,
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await _bases.GetAsync(knowledgeBaseId, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return KnowledgeBaseResult.Failure(
                ErrorCodes.KnowledgeBaseNotFound,
                "That knowledge base no longer exists.");
        }

        var trimmed = name.Trim();

        if (trimmed.Length > MaxNameLength)
        {
            return KnowledgeBaseResult.Failure(
                ErrorCodes.KnowledgeBaseNameTaken,
                $"A knowledge base name can be at most {MaxNameLength} characters.");
        }

        var bases = await _bases.ListAsync(cancellationToken).ConfigureAwait(false);
        if (bases.Any(candidate =>
            candidate.Id != knowledgeBaseId
            && string.Equals(candidate.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return KnowledgeBaseResult.Failure(
                ErrorCodes.KnowledgeBaseNameTaken,
                $"A knowledge base called \"{trimmed}\" already exists.");
        }

        await _bases.RenameAsync(knowledgeBaseId, trimmed, description, cancellationToken).ConfigureAwait(false);

        var renamed = await _bases.GetAsync(knowledgeBaseId, cancellationToken).ConfigureAwait(false);

        return renamed is null
            ? KnowledgeBaseResult.Failure(
                ErrorCodes.KnowledgeBaseNotFound,
                "That knowledge base no longer exists.")
            : KnowledgeBaseResult.Updated(renamed);
    }

    /// <summary>
    /// Removes a base and its index. The files it was built from are not touched, and this is
    /// said in the confirmation the page asks for, because "delete" on a page that lists people
    /// their documents quite reasonably reads as "delete my documents".
    /// </summary>
    public async Task<KnowledgeBaseResult> DeleteAsync(
        Guid knowledgeBaseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await _bases.GetAsync(knowledgeBaseId, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return KnowledgeBaseResult.Failure(
                ErrorCodes.KnowledgeBaseNotFound,
                "That knowledge base no longer exists.");
        }

        var removed = await _bases.DeleteAsync(knowledgeBaseId, cancellationToken).ConfigureAwait(false);

        return removed
            ? KnowledgeBaseResult.Removed()
            : KnowledgeBaseResult.Failure(
                ErrorCodes.KnowledgeBaseNotFound,
                "That knowledge base no longer exists.");
    }

    /// <summary>
    /// Gets the documents in a base, for the page's list.
    /// </summary>
    public Task<IReadOnlyList<KnowledgeDocument>> ListDocumentsAsync(
        Guid knowledgeBaseId,
        CancellationToken cancellationToken = default) =>
        _documents.ListAsync(knowledgeBaseId, cancellationToken);
}
