using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.AI.Services;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Tests.AI;

/// <summary>
/// What the coordinator promises, tested with providers that are under the test's control.
/// <para>
/// The coordinator is the one place that decides whether a request may be sent at all, so these
/// tests are mostly about refusals: an unknown provider, a cloud provider without permission, a
/// provider that hangs, a provider that fails. A happy path through this class would prove very
/// little on its own.
/// </para>
/// </summary>
public sealed class AIServiceTests
{
    private static readonly AIRequest Request = new(
        [AIMessage.CreateUser("Hello")],
        new AIRequestSettings { Timeout = TimeSpan.FromSeconds(30) });

    [Fact]
    public async Task SendMessageAsync_WithAKnownProvider_UsesItAndReportsIt()
    {
        var provider = new FakeAIProvider(AIProviderType.Mock);
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Mock };
        factory.Register(provider);
        var service = CreateService(factory);

        var response = await service.SendMessageAsync(Request);

        Assert.True(response.IsSuccessful);
        Assert.Equal(AIProviderType.Mock, response.Provider);
        Assert.Equal(1, provider.SendCount);
    }

    [Fact]
    public async Task ActiveProvider_ReportsTheConfiguredSelection()
    {
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Mock };
        factory.Register(new FakeAIProvider(AIProviderType.Mock));
        var service = CreateService(factory);

        Assert.Equal(AIProviderType.Mock, service.ActiveProvider);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SendMessageAsync_WithAnUnknownProvider_FailsWithASpecificCodeAndNeverCallsAProvider()
    {
        var provider = new FakeAIProvider();
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Unknown };
        factory.Register(provider);
        var service = CreateService(factory);

        var response = await service.SendMessageAsync(Request);

        Assert.False(response.IsSuccessful);
        Assert.Equal(ErrorCodes.AiProviderUnavailable, response.ErrorCode);
        Assert.Equal(0, provider.SendCount);
    }

    [Fact]
    public async Task SendMessageAsync_WithAMissingProvider_FailsRatherThanThrowing()
    {
        // The name resolves to nothing at all, which is what a provider removed from the build
        // looks like from here.
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Local };
        var service = CreateService(factory);

        var response = await service.SendMessageAsync(Request);

        Assert.False(response.IsSuccessful);
        Assert.Equal(ErrorCodes.AiProviderUnavailable, response.ErrorCode);
    }

    [Fact]
    public async Task SendMessageAsync_WithACloudProviderAndNoConsent_SendsNothing()
    {
        var provider = new FakeAIProvider(AIProviderType.OpenAI, isCloudHosted: true);
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.OpenAI };
        factory.Register(provider);
        var permissions = new FakePermissionService();
        permissions.Denied.Add(PermissionCapability.CloudAI);
        var service = CreateService(factory, permissions);

        var response = await service.SendMessageAsync(Request);

        Assert.False(response.IsSuccessful);
        Assert.Equal(ErrorCodes.AiCloudConsentRequired, response.ErrorCode);

        // The point of the test: nothing reached the provider, so nothing left the device.
        Assert.Equal(0, provider.SendCount);
    }

    [Fact]
    public async Task SendMessageAsync_WithACloudProviderAndConsent_UsesTheProvider()
    {
        var provider = new FakeAIProvider(AIProviderType.OpenAI, isCloudHosted: true);
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.OpenAI };
        factory.Register(provider);
        var service = CreateService(factory, new FakePermissionService());

        var response = await service.SendMessageAsync(Request);

        Assert.True(response.IsSuccessful);
        Assert.Equal(1, provider.SendCount);
    }

    [Fact]
    public async Task SendMessageAsync_WithALocalProvider_DoesNotNeedCloudConsent()
    {
        var provider = new FakeAIProvider(AIProviderType.Local, isCloudHosted: false);
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Local };
        factory.Register(provider);
        var permissions = new FakePermissionService();
        permissions.Denied.Add(PermissionCapability.CloudAI);
        var service = CreateService(factory, permissions);

        var response = await service.SendMessageAsync(Request);

        // Cloud consent is about data leaving the device. A local provider sends nothing, so
        // refusing it on that basis would block the one case that is private by definition.
        Assert.True(response.IsSuccessful);
    }

    [Fact]
    public async Task SendMessageAsync_WhenTheCallerCancels_RethrowsRatherThanReportingAFailure()
    {
        var provider = new FakeAIProvider
        {
            ExceptionToThrow = new OperationCanceledException(),
        };
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Mock };
        factory.Register(provider);
        var service = CreateService(factory);

        // A person pressing stop is not an error, and a caller that cannot tell the difference
        // would show "something went wrong" every time they cancel.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SendMessageAsync(Request));
    }

    [Fact]
    public async Task SendMessageAsync_WhenTheProviderFails_ReturnsTheProvidersCodeAndMessage()
    {
        var provider = new FakeAIProvider
        {
            ExceptionToThrow = new AIServiceException("The key was refused.", ErrorCodes.AiAuthenticationFailed),
        };
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Mock };
        factory.Register(provider);
        var service = CreateService(factory);

        var response = await service.SendMessageAsync(Request);

        Assert.False(response.IsSuccessful);
        Assert.Equal(ErrorCodes.AiAuthenticationFailed, response.ErrorCode);
        Assert.Equal("The key was refused.", response.ErrorMessage);
    }

    [Fact]
    public async Task SendMessageAsync_WhenTheProviderThrowsSomethingUnexpected_ReturnsSafeWording()
    {
        var provider = new FakeAIProvider
        {
            // A real exception leaks host names, type names, and request fields. None of that
            // may reach the person.
            ExceptionToThrow = new InvalidOperationException("endpoint internal-host-7 failed"),
        };
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Mock };
        factory.Register(provider);
        var service = CreateService(factory);

        var response = await service.SendMessageAsync(Request);

        Assert.False(response.IsSuccessful);
        Assert.Equal(ErrorCodes.AiRequestFailed, response.ErrorCode);
        Assert.DoesNotContain("internal-host-7", response.ErrorMessage);
    }

    [Fact]
    public async Task SendMessageAsync_WhenTheRequestOutlivesItsTimeout_ReportsATimeout()
    {
        // The provider waits for its token, so the deadline is genuinely what ends the request.
        var provider = new FakeAIProvider { HangUntilCancelled = true };
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Mock };
        factory.Register(provider);
        var service = CreateService(factory);

        var request = new AIRequest(
            [AIMessage.CreateUser("Hello")],
            new AIRequestSettings { Timeout = TimeSpan.FromMilliseconds(50) });
        var response = await service.SendMessageAsync(request);

        // A timeout is a failure with a code, not a cancellation. Reporting it as a cancellation
        // would tell a caller the person stopped it, which is both untrue and unactionable.
        Assert.False(response.IsSuccessful);
        Assert.Equal(ErrorCodes.AiTimedOut, response.ErrorCode);
    }

    [Fact]
    public async Task SendMessageAsync_WhenTheCallerCancelsWhileTheProviderIsWaiting_StillReportsCancellation()
    {
        var provider = new FakeAIProvider { HangUntilCancelled = true };
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Mock };
        factory.Register(provider);
        var service = CreateService(factory);

        var request = new AIRequest([AIMessage.CreateUser("Hello")]);
        using var cancellation = new CancellationTokenSource();

        var pending = service.SendMessageAsync(request, cancellation.Token);
        await cancellation.CancelAsync();

        // A deadline is generous here, so only the caller can be the reason it ends.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task SendMessageAsync_WithANullRequest_Throws()
    {
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Mock };
        factory.Register(new FakeAIProvider());
        var service = CreateService(factory);

        // Cast because the call takes three overloads and a bare null would be ambiguous.
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SendMessageAsync((AIRequest)null!));
    }

    [Fact]
    public async Task StreamMessageAsync_AnnouncesTheStartExactlyOnce()
    {
        var provider = new FakeAIProvider
        {
            Response = AIResponse.Success("One two three.", AIProviderType.Mock, "fake-model"),
            AnnouncesOwnStart = true,
        };
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Mock };
        factory.Register(provider);
        var service = CreateService(factory);

        var updates = await CollectAsync(service.StreamMessageAsync(Request));

        // Both the coordinator and the provider announce a start. A caller that grows a message
        // on the first one and treats the second as content would restart the message.
        Assert.Equal(1, updates.Count(update => update.Kind == AIStreamUpdateKind.Started));
    }

    [Fact]
    public async Task StreamMessageAsync_EmitsDeltasThatReassembleIntoTheAnswer()
    {
        const string answer = "The quick brown fox jumps over the lazy dog.";
        var provider = new FakeAIProvider
        {
            Response = AIResponse.Success(answer, AIProviderType.Mock, "fake-model"),
        };
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Mock };
        factory.Register(provider);
        var service = CreateService(factory);

        var updates = await CollectAsync(service.StreamMessageAsync(Request));

        var deltas = string.Concat(updates
            .Where(update => update.Kind == AIStreamUpdateKind.Delta)
            .Select(update => update.Text));

        // Whitespace included: a stream that drops a space produces answers that read wrong.
        Assert.Equal(answer, deltas);

        var completed = Assert.Single(updates, update => update.Kind == AIStreamUpdateKind.Completed);
        Assert.Equal(answer, completed.Text);
    }

    [Fact]
    public async Task StreamMessageAsync_WhenAProviderFailsPartWay_KeepsTheTextAlreadyReceived()
    {
        var provider = new FakeAIProvider
        {
            Response = AIResponse.Success("Half an answer that stops.", AIProviderType.Mock, "fake-model"),
            StreamExceptionAfterFirstDelta = new AIServiceException("Dropped.", ErrorCodes.AiNetworkFailure),
        };
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Mock };
        factory.Register(provider);
        var service = CreateService(factory);

        var updates = await CollectAsync(service.StreamMessageAsync(Request));

        var failure = Assert.Single(updates, update => update.Kind == AIStreamUpdateKind.Failed);
        Assert.Equal(ErrorCodes.AiNetworkFailure, failure.ErrorCode);

        // Whatever arrived is carried on the failure, so the transcript can show it rather than
        // throwing away a readable half-answer.
        Assert.NotEmpty(failure.Text);
        Assert.Contains("Half", failure.Text);
    }

    [Fact]
    public async Task StreamMessageAsync_WhenTheStreamEndsWithNothing_ReportsAnEmptyAnswer()
    {
        var provider = new FakeAIProvider
        {
            StreamUpdates = [],
        };
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Mock };
        factory.Register(provider);
        var service = CreateService(factory);

        var updates = await CollectAsync(service.StreamMessageAsync(Request));

        var failure = Assert.Single(updates, update => update.Kind == AIStreamUpdateKind.Failed);
        Assert.Equal(ErrorCodes.AiRequestFailed, failure.ErrorCode);
    }

    [Fact]
    public async Task StreamMessageAsync_WithACloudProviderAndNoConsent_FailsWithoutStarting()
    {
        var provider = new FakeAIProvider(AIProviderType.OpenAI, isCloudHosted: true);
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.OpenAI };
        factory.Register(provider);
        var permissions = new FakePermissionService();
        permissions.Denied.Add(PermissionCapability.CloudAI);
        var service = CreateService(factory, permissions);

        var updates = await CollectAsync(service.StreamMessageAsync(Request));

        Assert.Equal(0, provider.StreamCount);
        var failure = Assert.Single(updates);
        Assert.Equal(AIStreamUpdateKind.Failed, failure.Kind);
        Assert.Equal(ErrorCodes.AiCloudConsentRequired, failure.ErrorCode);
    }

    [Fact]
    public async Task StreamMessageAsync_WhenTheCallerCancels_StopsWithoutReportingAFailure()
    {
        var provider = new FakeAIProvider
        {
            Response = AIResponse.Success("A long answer that will be interrupted.", AIProviderType.Mock),
            FragmentDelay = TimeSpan.FromMilliseconds(40),
        };
        var factory = new FakeAIProviderFactory { SelectedProvider = AIProviderType.Mock };
        factory.Register(provider);
        var service = CreateService(factory);

        using var cancellation = new CancellationTokenSource();
        var seen = new List<AIStreamUpdate>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var update in service.StreamMessageAsync(Request, cancellation.Token))
            {
                seen.Add(update);

                if (seen.Count == 1)
                {
                    await cancellation.CancelAsync();
                }
            }
        });

        Assert.NotEmpty(seen);
        Assert.DoesNotContain(seen, update => update.Kind == AIStreamUpdateKind.Failed);
    }

    private static AIService CreateService(
        IAIProviderFactory factory,
        IPermissionService? permissions = null,
        IAIRequestDefaults? defaults = null) =>
        new(
            factory,
            defaults ?? AITestHarness.CreateDefaults(),
            permissions ?? new FakePermissionService(),
            NullLogger<AIService>.Instance);

    /// <summary>
    /// Drains a stream into a list. Enumerating to the end is what the handlers do, so this is
    /// the shape a test needs to assert on.
    /// </summary>
    private static async Task<List<AIStreamUpdate>> CollectAsync(IAsyncEnumerable<AIStreamUpdate> updates)
    {
        var collected = new List<AIStreamUpdate>();

        await foreach (var update in updates)
        {
            collected.Add(update);
        }

        return collected;
    }
}
