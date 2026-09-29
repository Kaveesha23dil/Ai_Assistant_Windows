using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Core.Models.Knowledge;

namespace WindowsAIAssistant.Application.Knowledge;

/// <summary>
/// Answers a question from a knowledge base, and is the only place that combines retrieval with
/// a model.
/// <para>
/// The order of the steps is the design. Retrieve first, so the model is never sent a question it
/// has nothing to answer from. Build the context from what survived retrieval, so a passage that
/// did not make it into the context cannot appear in the citation list. Check consent, and refuse
/// before the context exists if the text would have to leave the machine. Only then call the
/// single AI path, streaming included, so a spoken answer and a typed one go through the same
/// provider, the same key handling, and the same error translation.
/// </para>
/// <para>
/// The consent check is here and not in <see cref="AIService"/> because that service cannot know
/// what it has been handed. It sees a message; this sees a message that contains passages from a
/// person's documents, and those are two different permissions. Checking
/// <see cref="PermissionCapability.CloudKnowledgeProcessing"/> here and letting the ordinary cloud
/// check happen underneath means the stricter one cannot be skipped by a caller that already had
/// permission for chat.
/// </para>
/// <para>
/// Retrieval failures are returned rather than thrown, carrying a stable code. A person whose
/// index needs reindexing needs to be told that, and an exception is not a thing the interface
/// can put a sentence next to.
/// </para>
/// </summary>
public sealed class RagService : IRagService
{
    private readonly IAIService _ai;
    private readonly IPermissionService _permissions;
    private readonly ILogger<RagService> _logger;
    private readonly RagRetrievalPolicy _policy;
    private readonly IRagContextBuilder _contextBuilder;
    private readonly IRagRetriever _retriever;

    public RagService(
        IRagRetriever retriever,
        IRagContextBuilder contextBuilder,
        IAIService ai,
        IPermissionService permissions,
        RagRetrievalPolicy policy,
        ILogger<RagService> logger)
    {
        ArgumentNullException.ThrowIfNull(retriever);
        ArgumentNullException.ThrowIfNull(contextBuilder);
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(logger);

        _retriever = retriever;
        _contextBuilder = contextBuilder;
        _ai = ai;
        _permissions = permissions;
        _policy = policy;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        KnowledgeQuery query,
        CancellationToken cancellationToken = default) =>
        _retriever.RetrieveAsync(query, cancellationToken);

    /// <inheritdoc />
    public async Task<RagAnswer> AskAsync(KnowledgeQuery query, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(query, cancellationToken).ConfigureAwait(false);

        if (prepared.Failure is not null)
        {
            return prepared.Failure;
        }

        var messages = new[]
        {
            AIMessage.CreateUser(RagPromptBuilder.BuildQuestion(query.Question, prepared.Context!)),
        };

        var response = await _ai.SendMessageAsync(messages, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessful)
        {
            return RagAnswer.Failure(
                response.ErrorCode ?? ErrorCodes.KnowledgeAnswerFailed,
                response.ErrorMessage ?? "The assistant could not answer from your documents. Try again in a moment.");
        }

        return RagAnswer.FromContext(
            ReadAnswer(response.Content),
            prepared.Context!.Sources,
            prepared.RetrievedCount,
            prepared.Context.WasTruncated,
            prepared.LexicalOnly);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RagStreamUpdate> AskStreamingAsync(
        KnowledgeQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(query, cancellationToken).ConfigureAwait(false);

        if (prepared.Failure is not null)
        {
            yield return RagStreamUpdate.Failed(prepared.Failure.ErrorCode!, prepared.Failure.ErrorMessage!);
            yield break;
        }

        // The sources are announced before any text, because the search is already over and the
        // person can see which documents are about to be quoted while the answer is still being
        // written. Doing it the other way round would mean the answer existed before anything
        // said where it came from.
        yield return RagStreamUpdate.SourcesFound(
            prepared.Context!.Sources,
            prepared.RetrievedCount,
            prepared.LexicalOnly);

        var request = new AIRequest(
        [
            AIMessage.CreateUser(RagPromptBuilder.BuildQuestion(query.Question, prepared.Context)),
        ]);

        var answer = new StringBuilder();

        await foreach (var update in _ai.StreamMessageAsync(request, cancellationToken).ConfigureAwait(false))
        {
            switch (update.Kind)
            {
                case AIStreamUpdateKind.Delta:
                    answer.Append(update.Text);
                    yield return RagStreamUpdate.Delta(update.Text);
                    break;

                case AIStreamUpdateKind.Completed:
                    yield return RagStreamUpdate.Completed(
                        RagAnswer.FromContext(
                            ReadAnswer(string.IsNullOrEmpty(update.Text) ? answer.ToString() : update.Text),
                            prepared.Context.Sources,
                            prepared.RetrievedCount,
                            prepared.Context.WasTruncated,
                            prepared.LexicalOnly));
                    break;

                case AIStreamUpdateKind.Failed:
                    // Whatever had already arrived is kept and reported. Discarding it would
                    // throw away a partially-correct answer because the last sentence failed, and
                    // the person is left with nothing at all.
                    yield return RagStreamUpdate.Failed(
                        update.ErrorCode ?? ErrorCodes.KnowledgeAnswerFailed,
                        update.ErrorMessage ?? "The assistant could not finish the answer.",
                        answer.ToString());
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Runs everything that has to happen before a word of the answer exists.
    /// </summary>
    /// <returns>
    /// Either a failure to return as it is, or the built context and the two facts that must
    /// travel with the answer: how many passages were retrieved, and whether they were found by
    /// wording alone.
    /// </returns>
    private async Task<Prepared> PrepareAsync(KnowledgeQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<KnowledgeSearchResult> retrieved;

        try
        {
            retrieved = await _retriever.RetrieveAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (KnowledgeException exception)
        {
            _logger.LogWarning(
                "A knowledge question could not be answered because retrieval failed with {ErrorCode}.",
                exception.ErrorCode);

            return Prepared.Failed(RagAnswer.Failure(
                exception.ErrorCode ?? ErrorCodes.KnowledgeOperationFailed,
                exception.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Nothing from here reaches the person. A repository or provider type name in a chat
            // bubble tells a reader nothing useful and tells a reader with logs something more.
            _logger.LogError(exception, "A knowledge question failed unexpectedly.");
            return Prepared.Failed(RagAnswer.Failure(
                ErrorCodes.KnowledgeOperationFailed,
                "The assistant could not search your documents. Try again in a moment."));
        }

        if (retrieved.Count == 0)
        {
            return Prepared.Succeeded(RagAnswer.NoMatches(RagPromptBuilder.NoMatchesMessage));
        }

        var lexicalOnly = retrieved.All(result => !result.HasVectorScore);

        if (RequiresCloudConsent())
        {
            // Refused before the context is built, so the passages are never assembled into
            // something that could be sent by accident and never reach a provider.
            _logger.LogInformation(
                "A knowledge answer was not generated because the retrieved passages would have to be sent to a provider.");

            return Prepared.Succeeded(RagAnswer.PermissionDenied(
                _permissions.GetDeniedMessage(PermissionCapability.CloudKnowledgeProcessing),
                retrieved.Count));
        }

        var context = _contextBuilder.Build(retrieved, _policy.MaximumContextCharacters);

        if (context.IsEmpty)
        {
            return Prepared.Succeeded(RagAnswer.NoMatches(RagPromptBuilder.NoMatchesMessage));
        }

        return Prepared.Succeeded(null, context, retrieved.Count, lexicalOnly);
    }

    /// <summary>
    /// Whether answering this question would put a person's document text in front of a provider
    /// that is not on this machine.
    /// <para>
    /// Asked of the AI provider's own locality rather than of a configured name, so a provider
    /// that turns out to be cloud-hosted cannot be reached by naming it differently. The
    /// embedding permission is not asked here: by the time an answer is being produced, the
    /// embeddings were made under their own check, and re-asking would refuse a question whose
    /// vectors were made locally and whose only remaining act is to write an answer.
    /// </para>
    /// </summary>
    private bool RequiresCloudConsent() =>
        _ai.ActiveProvider is not AIProviderType.Unknown
        && !AIProviderTypes.IsLocal(_ai.ActiveProvider)
        && !_permissions.IsGranted(PermissionCapability.CloudKnowledgeProcessing);

    /// <summary>
    /// Turns a model's answer into the text a person sees.
    /// <para>
    /// The refusal marker is turned into a sentence rather than shown as-is, because
    /// <c>DOCUMENT_ONLY</c> is an instruction to the model and not a thing anybody asked to read.
    /// A model that ignores the instruction and says "it is not in the documents" is left alone
    /// — the sentence is already right, and rewriting it would be guessing.
    /// </para>
    /// </summary>
    private static string ReadAnswer(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return RagPromptBuilder.NoMatchesMessage;
        }

        var trimmed = content.Trim();

        return trimmed.Equals(RagPromptBuilder.SourceNotFoundMarker, StringComparison.OrdinalIgnoreCase)
            ? RagPromptBuilder.NoMatchesMessage
            : trimmed;
    }

    /// <summary>
    /// The result of the work that happens before an answer exists: either a failure to return
    /// as it is, or the context and the facts that must travel with the answer.
    /// </summary>
    private readonly record struct Prepared(
        RagAnswer? Failure,
        RagContext? Context,
        int RetrievedCount,
        bool LexicalOnly)
    {
        public static Prepared Failed(RagAnswer failure) => new(failure, null, 0, false);

        public static Prepared Succeeded(
            RagAnswer? failure = null,
            RagContext? context = null,
            int retrievedCount = 0,
            bool lexicalOnly = false) =>
            new(failure, context, retrievedCount, lexicalOnly);
    }
}
