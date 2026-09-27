using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.AI.Services;

/// <summary>
/// The one place a request is prepared, checked, bounded, and turned back into something safe
/// to show.
/// <para>
/// Everything above this class — the typed chat page, the voice question handler, the
/// clipboard summariser — depends on <see cref="IAIService"/> and knows nothing about
/// providers, keys, or configuration. That is what makes three properties of the design true
/// at once instead of by convention: there is a single AI path, so voice and typing cannot
/// drift apart; a cloud provider is contacted only after the consent switch is checked here
/// rather than by each caller; and a vendor error is translated once, so a raw SDK message
/// cannot leak into the interface or a spoken reply.
/// </para>
/// <para>
/// Failures are returned, not thrown. A missing key, a refused consent switch, a rate limit,
/// and a typo in the provider name are all ordinary outcomes a person can act on, and each
/// comes back as a failed <see cref="AIResponse"/> carrying a stable code and a sentence meant
/// for a person. Cancellation is the exception: it is the caller's own decision, so it is
/// rethrown for the caller to recognise.
/// </para>
/// </summary>
public sealed class AIService : IAIService
{
    /// <summary>
    /// The time budget used when neither the request nor configuration supplies one. A
    /// provider that stops answering must not be able to hold the application open.
    /// </summary>
    private static readonly TimeSpan FallbackTimeout = TimeSpan.FromSeconds(60);

    private readonly IAIProviderFactory _providers;
    private readonly IAIRequestDefaults _defaults;
    private readonly IPermissionService _permissions;
    private readonly ILogger<AIService> _logger;

    public AIService(
        IAIProviderFactory providers,
        IAIRequestDefaults defaults,
        IPermissionService permissions,
        ILogger<AIService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(logger);

        _providers = providers;
        _defaults = defaults;
        _permissions = permissions;
        _logger = logger;
    }

    /// <inheritdoc />
    public AIProviderType ActiveProvider => _providers.SelectedProvider;

    /// <inheritdoc />
    public Task<AIResponse> SendMessageAsync(
        IReadOnlyCollection<AIMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return SendMessageAsync(new AIRequest(messages), cancellationToken);
    }

    /// <inheritdoc />
    public Task<AIResponse> SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return SendMessageAsync(AIRequest.FromMessage(message), cancellationToken);
    }

    /// <summary>
    /// Sends a prepared request, which is how a caller that has already built a conversation
    /// of its own reaches the same path a single message takes.
    /// </summary>
    public async Task<AIResponse> SendMessageAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = ResolveProvider();
        if (provider is null)
        {
            return ProviderUnavailable();
        }

        if (IsConsentMissing(provider))
        {
            return CloudConsentRequired(provider.ProviderType);
        }

        using var budget = TimeoutBudget.Create(request.Settings.Timeout, _defaults.RequestTimeout, cancellationToken);

        try
        {
            var response = await provider.SendMessageAsync(request, budget.Token).ConfigureAwait(false);
            LogOutcome(response.IsSuccessful, response.ErrorCode, provider.ProviderType);
            return response;
        }
        catch (OperationCanceledException) when (budget.Expired)
        {
            return TimedOut(provider.ProviderType);
        }
        catch (OperationCanceledException)
        {
            // The caller asked to stop. That is not a failure to report, and rethrowing keeps
            // "cancelled" visibly different from "failed".
            throw;
        }
        catch (AIServiceException exception)
        {
            // The provider already reduced the failure to a code and a sentence written for a
            // person, so only the code is logged. A message is user-facing wording and adding
            // a second copy to the log would be noise at best.
            _logger.LogWarning(
                "AI request failed with code {ErrorCode} using provider {Provider}.",
                exception.ErrorCode,
                provider.ProviderType);

            return AIResponse.Failure(
                exception.Message,
                provider.ProviderType,
                exception.ErrorCode ?? ErrorCodes.AiRequestFailed,
                Model);
        }
        catch (Exception exception)
        {
            // Nothing from the exception reaches the person: it may name an internal type, a
            // host, or a request field. The exception itself is recorded for support.
            _logger.LogError(
                exception,
                "AI request failed unexpectedly using provider {Provider}.",
                provider.ProviderType);

            return AIResponse.Failure(
                "The assistant could not complete that request. Try again in a moment.",
                provider.ProviderType,
                ErrorCodes.AiRequestFailed,
                Model);
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AIStreamUpdate> StreamMessageAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return StreamCoreAsync(request, cancellationToken);
    }

    private async IAsyncEnumerable<AIStreamUpdate> StreamCoreAsync(
        AIRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var provider = ResolveProvider();
        if (provider is null)
        {
            var unavailable = ProviderUnavailable();
            yield return AIStreamUpdate.Failed(
                string.Empty,
                unavailable,
                unavailable.ErrorCode!,
                unavailable.ErrorMessage);
            yield break;
        }

        if (IsConsentMissing(provider))
        {
            var denied = CloudConsentRequired(provider.ProviderType);
            yield return AIStreamUpdate.Failed(
                string.Empty,
                denied,
                denied.ErrorCode!,
                denied.ErrorMessage);
            yield break;
        }

        using var budget = TimeoutBudget.Create(
            request.Settings.Timeout,
            _defaults.RequestTimeout,
            cancellationToken);

        var text = new StringBuilder();
        AIStreamUpdate? failure = null;
        var finished = false;
        IAsyncEnumerator<AIStreamUpdate>? updates = null;

        yield return AIStreamUpdate.Started(provider.ProviderType, Model);

        try
        {
            try
            {
                updates = provider
                    .StreamMessageAsync(request, budget.Token)
                    .GetAsyncEnumerator(budget.Token);
            }
            catch (Exception exception)
            {
                failure = DescribeFailure(exception, provider, budget.Expired, text.ToString());
            }

            while (failure is null && !finished)
            {
                bool hasNext;
                try
                {
                    hasNext = await updates!.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failure = DescribeFailure(exception, provider, budget.Expired, text.ToString());
                    break;
                }

                if (!hasNext)
                {
                    break;
                }

                var update = updates.Current;

                switch (update.Kind)
                {
                    case AIStreamUpdateKind.Delta:
                        text.Append(update.Text);
                        yield return update;
                        break;

                    case AIStreamUpdateKind.Completed:
                        text.Clear().Append(update.Text);
                        finished = true;
                        yield return update;
                        break;

                    case AIStreamUpdateKind.Failed:
                        // A provider that reports its own early stop already knows the partial
                        // text, so the update is passed through rather than rebuilt.
                        failure = update;
                        finished = true;
                        break;

                    case AIStreamUpdateKind.Started:
                        // Absorbed on purpose. The start is announced once, above, and only
                        // after the provider resolved and consent was confirmed. Passing a
                        // provider's own start through would report a second start for one
                        // request, which is worse than losing it: a caller that starts a message
                        // on the first one would then treat the second as new output.
                        break;

                    default:
                        // A kind this version does not know is still someone's real update, so
                        // it is passed along rather than swallowed. The transcript is written
                        // against the kinds it knows, so an unknown one is inert there.
                        yield return update;
                        break;
                }
            }
        }
        finally
        {
            if (updates is not null)
            {
                await updates.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (failure is not null)
        {
            yield return failure;
            yield break;
        }

        if (!finished)
        {
            // The stream ended without a final update. Whatever arrived is still the answer, so
            // it is completed rather than discarded; an empty stream is reported as a failure
            // because there is nothing to show.
            yield return text.Length > 0
                ? AIStreamUpdate.Completed(
                    AIResponse.Success(text.ToString(), provider.ProviderType, Model),
                    text.ToString())
                : AIStreamUpdate.Failed(
                    string.Empty,
                    AIResponse.Failure(
                        "The assistant returned an empty answer. Try asking in a different way.",
                        provider.ProviderType,
                        ErrorCodes.AiRequestFailed,
                        Model),
                    ErrorCodes.AiRequestFailed,
                    "The assistant returned an empty answer. Try asking in a different way.");
        }
    }

    /// <summary>
    /// Reduces an exception to a failure update, keeping the text that had already arrived.
    /// </summary>
    private AIStreamUpdate DescribeFailure(
        Exception exception,
        IAIProvider provider,
        bool expired,
        string text)
    {
        if (exception is OperationCanceledException)
        {
            if (!expired)
            {
                // The caller's own cancellation, rethrown so a stop is distinguishable from a
                // failure. The text it has already collected is its own to keep.
                throw exception;
            }

            return Failed(text, provider.ProviderType, ErrorCodes.AiTimedOut, TimeoutMessage);
        }

        var classified = exception as AIServiceException;
        var code = classified?.ErrorCode ?? ErrorCodes.AiRequestFailed;
        var message = classified?.Message ?? RequestFailedMessage;

        if (classified is null)
        {
            _logger.LogError(exception, "AI streaming request failed unexpectedly.");
        }
        else
        {
            _logger.LogWarning("AI streaming request failed with code {ErrorCode}.", code);
        }

        return Failed(text, provider.ProviderType, code, message);
    }

    private void LogOutcome(bool successful, string? errorCode, AIProviderType provider)
    {
        if (successful)
        {
            _logger.LogInformation("AI request completed using provider {Provider}.", provider);
            return;
        }

        _logger.LogWarning(
            "AI request failed with code {ErrorCode} using provider {Provider}.",
            errorCode,
            provider);
    }

    private AIStreamUpdate Failed(
        string text,
        AIProviderType provider,
        string code,
        string message) =>
        AIStreamUpdate.Failed(
            text,
            AIResponse.Failure(message, provider, code, Model),
            code,
            message);

    private IAIProvider? ResolveProvider() => _providers.ResolveSelected();

    private bool IsConsentMissing(IAIProvider provider) =>
        provider.IsCloudHosted && !_permissions.IsGranted(PermissionCapability.CloudAI);

    private string? Model => _defaults.Model;

    private static AIResponse ProviderUnavailable() => AIResponse.Failure(
        "No AI provider is available for the configured selection. Choose a provider in Settings.",
        AIProviderType.Unknown,
        ErrorCodes.AiProviderUnavailable);

    private AIResponse CloudConsentRequired(AIProviderType provider) => AIResponse.Failure(
        "Cloud AI is turned off. Turn it on in Settings to let the assistant send questions to a provider.",
        provider,
        ErrorCodes.AiCloudConsentRequired,
        Model);

    private AIResponse TimedOut(AIProviderType provider) => AIResponse.Failure(
        TimeoutMessage,
        provider,
        ErrorCodes.AiTimedOut,
        Model);

    private const string TimeoutMessage =
        "The assistant took too long to answer. Try again or ask for something shorter.";

    private const string RequestFailedMessage =
        "The assistant could not complete that request. Try again in a moment.";

    /// <summary>
    /// The time budget for one request.
    /// <para>
    /// The caller's token and the timeout are linked, so whichever fires first ends the request
    /// and the other is left unregistered. The budget also records that it was the budget that
    /// expired, which is the only way to tell "the person pressed stop" from "the provider was
    /// too slow": both arrive as a cancelled operation.
    /// </para>
    /// <para>
    /// Expiry is read from the deadline's own token rather than from a flag set in a
    /// cancellation callback. A callback runs on whatever thread cancelled, and the code
    /// waiting on the linked token can wake before it, so a flag written there was read as
    /// "not expired" for a request that had genuinely run out of time. The token's own state
    /// is set before any waiter is signalled, so reading it cannot lose that race.
    /// </para>
    /// </summary>
    private sealed class TimeoutBudget : IDisposable
    {
        private readonly CancellationTokenSource _linked;
        private readonly CancellationTokenSource _deadline;

        private TimeoutBudget(CancellationTokenSource linked, CancellationTokenSource deadline)
        {
            _linked = linked;
            _deadline = deadline;
        }

        /// <summary>Gets the token every call made under this budget carries.</summary>
        public CancellationToken Token => _linked.Token;

        /// <summary>
        /// Gets a value indicating whether the budget ended the operation rather than the
        /// caller. A caller-initiated cancellation is never reported as a timeout, so the two
        /// stay distinguishable.
        /// </summary>
        public bool Expired => _deadline.IsCancellationRequested;

        public static TimeoutBudget Create(
            TimeSpan? requested,
            TimeSpan configured,
            CancellationToken cancellationToken)
        {
            var timeout = requested ?? configured;
            if (timeout <= TimeSpan.Zero)
            {
                timeout = FallbackTimeout;
            }

            var deadline = new CancellationTokenSource();
            deadline.CancelAfter(timeout);

            // Disposing the linked source is what releases its registration on the caller's
            // token, so the budget's own Dispose is enough to leave nothing behind on a token
            // that may outlive this request.
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

            return new TimeoutBudget(linked, deadline);
        }

        public void Dispose()
        {
            _linked.Dispose();
            _deadline.Dispose();
        }
    }
}
