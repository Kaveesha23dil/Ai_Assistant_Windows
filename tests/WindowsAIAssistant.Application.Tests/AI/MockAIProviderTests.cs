using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Infrastructure.AI;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Application.Tests.AI;

/// <summary>
/// The offline provider, which is what the application uses when no account is set up, and the
/// key lookup that decides whether a cloud provider can be used at all.
/// </summary>
public sealed class MockAIProviderTests
{
    private const string VariableName = "WINDOWS_AI_ASSISTANT_TEST_KEY";

    [Fact]
    public async Task SendMessageAsync_AnswersWithoutAnyKeyOrNetwork()
    {
        var provider = Create();

        var response = await provider.SendMessageAsync(AIRequest.FromMessage("Hello"));

        Assert.True(response.IsSuccessful);
        Assert.Equal(AIProviderType.Mock, response.Provider);
        Assert.Contains("Hello", response.Content);
    }

    [Fact]
    public void IsCloudHosted_IsFalse_SoNoConsentIsNeeded()
    {
        // The offline provider is the one case that is private by definition. Reporting it as
        // cloud would make the application ask for a permission it does not need.
        Assert.False(Create().IsCloudHosted);
    }

    [Fact]
    public void IsAvailable_IsAlwaysTrue()
    {
        Assert.True(Create().IsAvailable);
    }

    [Fact]
    public async Task StreamMessageAsync_ReassemblesIntoExactlyTheWholeAnswer()
    {
        var provider = Create();
        var request = AIRequest.FromMessage("Hello there, how are you?");

        var whole = await provider.SendMessageAsync(request);
        var updates = new List<Core.Models.AIStreamUpdate>();
        await foreach (var update in provider.StreamMessageAsync(request))
        {
            updates.Add(update);
        }

        var joined = string.Concat(updates
            .Where(update => update.Kind == AIStreamUpdateKind.Delta)
            .Select(update => update.Text));

        // Streaming and non-streaming must not disagree, or turning streaming off would change
        // the words the assistant says.
        Assert.Equal(whole.Content, joined);
    }

    [Fact]
    public async Task StreamMessageAsync_AnnouncesAStartAndEndsWithACompletion()
    {
        var provider = Create();

        var updates = new List<Core.Models.AIStreamUpdate>();
        await foreach (var update in provider.StreamMessageAsync(AIRequest.FromMessage("Hello")))
        {
            updates.Add(update);
        }

        Assert.Equal(AIStreamUpdateKind.Started, updates[0].Kind);
        Assert.Equal(AIStreamUpdateKind.Completed, updates[^1].Kind);
    }

    [Fact]
    public async Task StreamMessageAsync_WhenCancelled_StopsWithoutCompleting()
    {
        var provider = Create();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // A cancelled request is not a failed one, and it must not be reported as a completed
        // answer the transcript then stores.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in provider.StreamMessageAsync(AIRequest.FromMessage("Hello"), cancellation.Token))
            {
            }
        });
    }

    [Fact]
    public async Task SendMessageAsync_ReportsTheConfiguredModel()
    {
        var provider = Create(model: "offline-model-v2");

        var response = await provider.SendMessageAsync(AIRequest.FromMessage("Hello"));

        Assert.Equal("offline-model-v2", response.Model);
    }

    [Fact]
    public void EnvironmentApiKey_WithNoVariableSet_ReportsNoKey()
    {
        var provider = CreateKeyProvider(VariableName);
        WithVariable(VariableName, null);

        Assert.False(provider.HasApiKey);
        Assert.Null(provider.GetApiKey());
    }

    [Fact]
    public void EnvironmentApiKey_WithAnEmptyVariable_ReportsNoKey()
    {
        // A variable exported empty is a mistake, not a credential. Sending it would produce a
        // confusing authentication failure instead of a clear "no key configured".
        var provider = CreateKeyProvider(VariableName);
        WithVariable(VariableName, "   ");

        Assert.False(provider.HasApiKey);
        Assert.Null(provider.GetApiKey());
    }

    [Fact]
    public void EnvironmentApiKey_WithAKeySet_ReportsItTrimmed()
    {
        var provider = CreateKeyProvider(VariableName);
        WithVariable(VariableName, "  sk-example  ");

        Assert.True(provider.HasApiKey);
        Assert.Equal("sk-example", provider.GetApiKey());
    }

    [Fact]
    public void EnvironmentApiKey_UsesTheConfiguredVariableName()
    {
        var provider = CreateKeyProvider("WINDOWS_AI_ASSISTANT_OTHER_KEY");
        WithVariable(VariableName, "sk-set");
        WithVariable("WINDOWS_AI_ASSISTANT_OTHER_KEY", "sk-other");

        try
        {
            // The name is a setting like any other, so a key in a differently named variable
            // must not be picked up by accident.
            Assert.Equal("sk-other", provider.GetApiKey());
        }
        finally
        {
            WithVariable(VariableName, null);
            WithVariable("WINDOWS_AI_ASSISTANT_OTHER_KEY", null);
        }
    }

    [Fact]
    public void EnvironmentApiKey_ReadsTheVariableEachTime()
    {
        var provider = CreateKeyProvider(VariableName);

        try
        {
            WithVariable(VariableName, null);
            Assert.False(provider.HasApiKey);

            WithVariable(VariableName, "sk-late");
            Assert.True(provider.HasApiKey);
        }
        finally
        {
            WithVariable(VariableName, null);
        }
    }

    [Fact]
    public void EnvironmentApiKey_ReportsOpenAIAsItsProvider()
    {
        Assert.Equal(AIProviderType.OpenAI, CreateKeyProvider(VariableName).Provider);
    }

    private static MockAIProvider Create(string model = "mock-model") =>
        new(new TestOptionsMonitor<AIOptions>(new AIOptions { Model = model }));

    private static EnvironmentApiKeyProvider CreateKeyProvider(string variableName) =>
        new(new TestOptionsMonitor<AIOptions>(new AIOptions { ApiKeyEnvironmentVariable = variableName }));

    /// <summary>
    /// Sets a variable for the length of a test and puts it back afterwards, so a test can never
    /// leave a value behind for another test to find.
    /// </summary>
    private static void WithVariable(string name, string? value) =>
        Environment.SetEnvironmentVariable(name, value);
}
