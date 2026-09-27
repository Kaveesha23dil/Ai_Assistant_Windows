using System.ClientModel;
using System.Net;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Infrastructure.AI;

namespace WindowsAIAssistant.Application.Tests.AI;

/// <summary>
/// How a cloud failure is described.
/// <para>
/// The wording matters as much as the code. A refusal to say "check the model name" or "wait a
/// moment" leaves a person with no idea what to do, and a message carrying an internal host or
/// type name is worse still. These tests hold both halves of that line.
/// </para>
/// </summary>
public sealed class OpenAIErrorMapperTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Classify_WithARefusedKey_ReportsAuthentication(HttpStatusCode status)
    {
        var failure = Classify(status);

        Assert.Equal(ErrorCodes.AiAuthenticationFailed, failure.ErrorCode);
        Assert.Contains("API key", failure.Message);
    }

    [Fact]
    public void Classify_WithTooManyRequests_ReportsRateLimitingAndSuggestsWaiting()
    {
        var failure = Classify(HttpStatusCode.TooManyRequests);

        Assert.Equal(ErrorCodes.AiRateLimited, failure.ErrorCode);
        Assert.Contains("wait", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData((HttpStatusCode)504)]
    public void Classify_WithATimeoutStatus_ReportsATimeout(HttpStatusCode status)
    {
        var failure = Classify(status);

        Assert.Equal(ErrorCodes.AiTimedOut, failure.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public void Classify_WithARejectedRequest_PointsAtTheModelName(HttpStatusCode status)
    {
        var failure = Classify(status);

        // A wrong model name is by far the most common cause, so the message names it rather
        // than leaving a person to guess at a generic rejection.
        Assert.Equal(ErrorCodes.AiInvalidRequest, failure.ErrorCode);
        Assert.Contains("model", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_WithAnUndocumentedStatus_FallsBackToTheGenericFailure()
    {
        // Guessing at a status the provider did not document would risk telling a person the
        // wrong thing about their own account.
        var failure = Classify((HttpStatusCode)418);

        Assert.Equal(ErrorCodes.AiRequestFailed, failure.ErrorCode);
    }

    [Fact]
    public void Classify_WithAServerError_FallsBackToTheGenericFailure()
    {
        var failure = Classify(HttpStatusCode.InternalServerError);

        Assert.Equal(ErrorCodes.AiRequestFailed, failure.ErrorCode);
    }

    [Fact]
    public void Classify_WithACancelledOperation_ReportsATimeout()
    {
        var failure = OpenAIErrorMapper.Classify(new TaskCanceledException());

        Assert.NotNull(failure);
        Assert.Equal(ErrorCodes.AiTimedOut, failure.ErrorCode);
    }

    [Fact]
    public void Classify_WithAConnectionFailure_ReportsANetworkProblem()
    {
        var failure = OpenAIErrorMapper.Classify(new HttpRequestException("no such host is known"));

        Assert.NotNull(failure);
        Assert.Equal(ErrorCodes.AiNetworkFailure, failure.ErrorCode);
    }

    [Fact]
    public void Classify_WithAStreamFailure_ReportsANetworkProblem()
    {
        var failure = OpenAIErrorMapper.Classify(new IOException("the connection was reset"));

        Assert.NotNull(failure);
        Assert.Equal(ErrorCodes.AiNetworkFailure, failure.ErrorCode);
    }

    [Fact]
    public void Classify_WithSomethingUnrecognised_ReportsNothing()
    {
        // Returning null lets the caller handle it. Guessing a classification here would let a
        // genuine defect be reported to a person as a provider problem.
        Assert.Null(OpenAIErrorMapper.Classify(new InvalidOperationException("something else")));
    }

    [Fact]
    public void Classify_WithANullException_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OpenAIErrorMapper.Classify(null!));
    }

    [Fact]
    public void Classify_NeverLeaksTheVendorsOwnWords()
    {
        // The real exception text names hosts, paths, and identifiers the provider returned.
        // None of it may reach a person.
        var failure = Classify(HttpStatusCode.Unauthorized);

        Assert.DoesNotContain("api.openai.com", failure.Message);
        Assert.DoesNotContain("401", failure.Message);
        Assert.DoesNotContain("Traceback", failure.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData((HttpStatusCode)418)]
    public void FromStatus_NeverMentionsAHostPathOrStatusCode(HttpStatusCode status)
    {
        var message = Classify(status).Message;

        Assert.DoesNotContain("http", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/v1/", message);
    }

    private static AIServiceException Classify(HttpStatusCode status) =>
        OpenAIErrorMapper.FromStatus((int)status);
}
