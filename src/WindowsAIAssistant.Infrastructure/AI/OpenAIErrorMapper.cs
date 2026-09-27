using System.ClientModel;
using System.Net;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Exceptions;

namespace WindowsAIAssistant.Infrastructure.AI;

/// <summary>
/// Turns a provider failure into a code and a sentence a person can act on.
/// <para>
/// This is the boundary that keeps the vendor out of the interface. An SDK exception carries
/// the request that failed, the host that was contacted, and internal type names; none of that
/// belongs in a chat window or in a spoken reply, and a great deal of it should not be written
/// to a log either. Everything is classified here and only the classification leaves.
/// </para>
/// <para>
/// The HTTP status is the useful signal, so the mapping is by status rather than by exception
/// text. A status the provider did not document falls through to the generic request failure
/// rather than being guessed at.
/// </para>
/// </summary>
public static class OpenAIErrorMapper
{
    /// <summary>
    /// Reduces an exception to a user-safe failure, or returns <see langword="null"/> when the
    /// exception is not a provider failure and the caller should handle it.
    /// </summary>
    public static AIServiceException? Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            // A cancellation that was not requested is the provider taking too long. One that
            // was requested is the person's own decision and is not a failure at all.
            TaskCanceledException or OperationCanceledException => new AIServiceException(
                "The assistant took too long to answer. Try again or ask for something shorter.",
                ErrorCodes.AiTimedOut),

            ClientResultException result => FromStatus(result.Status),
            HttpRequestException => new AIServiceException(
                "The assistant could not reach the AI provider. Check your connection and try again.",
                ErrorCodes.AiNetworkFailure,
                exception),

            // The SDK reports a refused connection this way once the pipeline has run out of
            // retries, so it is a network outcome rather than a protocol one.
            IOException => new AIServiceException(
                "The assistant could not reach the AI provider. Check your connection and try again.",
                ErrorCodes.AiNetworkFailure,
                exception),

            _ => null
        };
    }

    /// <summary>
    /// Classifies a response status into a user-safe failure.
    /// <para>
    /// This step is public and separate from <see cref="Classify"/> because it is a pure
    /// function of a number: it is the table of what each status means to a person, and it can
    /// be checked directly rather than only through an SDK exception that is awkward to
    /// construct. Nothing here reaches the vendor, and a status that is not in the table is not
    /// guessed at.
    /// </para>
    /// </summary>
    public static AIServiceException FromStatus(int status) => status switch
    {
        (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden => new AIServiceException(
            "The AI provider rejected the API key. Check OPENAI_API_KEY and try again.",
            ErrorCodes.AiAuthenticationFailed),

        (int)HttpStatusCode.TooManyRequests => new AIServiceException(
            "The AI provider is busy right now. Wait a moment and try again.",
            ErrorCodes.AiRateLimited),

        (int)HttpStatusCode.RequestTimeout or 504 => new AIServiceException(
            "The assistant took too long to answer. Try again or ask for something shorter.",
            ErrorCodes.AiTimedOut),

        (int)HttpStatusCode.BadRequest
            or (int)HttpStatusCode.NotFound
            or (int)HttpStatusCode.Conflict
            or (int)HttpStatusCode.UnprocessableEntity => new AIServiceException(
            "The AI provider would not accept that request. Check the model name in Settings.",
            ErrorCodes.AiInvalidRequest),

        _ => new AIServiceException(
            "The assistant could not complete that request. Try again in a moment.",
            ErrorCodes.AiRequestFailed)
    };
}
