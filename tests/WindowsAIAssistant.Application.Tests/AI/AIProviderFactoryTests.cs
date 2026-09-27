using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.AI;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Application.Tests.AI;

/// <summary>
/// How a configured provider name becomes a provider, and what happens when it cannot.
/// </summary>
public sealed class AIProviderFactoryTests
{
    [Fact]
    public void ResolveSelected_UsesTheConfiguredProvider()
    {
        var mock = new FakeAIProvider(AIProviderType.Mock);
        var openAi = new FakeAIProvider(AIProviderType.OpenAI);
        var factory = Create([mock, openAi], provider: "Mock");

        Assert.Same(mock, factory.ResolveSelected());
        Assert.Equal(AIProviderType.Mock, factory.SelectedProvider);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("OpenAI")]
    [InlineData("  OpenAI  ")]
    public void SelectedProvider_IgnoresCaseAndSurroundingSpace(string configured)
    {
        var factory = Create([new FakeAIProvider(AIProviderType.OpenAI)], configured);

        // A value typed by hand into settings should not need to match a declaration exactly.
        Assert.Equal(AIProviderType.OpenAI, factory.SelectedProvider);
    }

    [Fact]
    public void SelectedProvider_WithANameThisBuildCannotServe_IsUnknown()
    {
        var factory = Create([new FakeAIProvider(AIProviderType.Mock)], "SomeOtherCloud");

        // Unknown rather than an exception: a name in configuration is not a reason to refuse to
        // start, and the person is told the choice needs fixing when they ask a question.
        Assert.Equal(AIProviderType.Unknown, factory.SelectedProvider);
        Assert.Null(factory.ResolveSelected());
    }

    [Fact]
    public void SelectedProvider_WithAnEmptyValue_IsUnknown()
    {
        var factory = Create([new FakeAIProvider(AIProviderType.Mock)], "   ");

        Assert.Equal(AIProviderType.Unknown, factory.SelectedProvider);
    }

    [Fact]
    public void Resolve_WithAnUnknownType_ProducesNothing()
    {
        var factory = Create([new FakeAIProvider(AIProviderType.Mock)], "Mock");

        Assert.Null(factory.Resolve(AIProviderType.Unknown));
    }

    [Fact]
    public void Resolve_WithATypeThatWasNotRegistered_ProducesNothing()
    {
        var factory = Create([new FakeAIProvider(AIProviderType.Mock)], "Mock");

        // Registered and selected are different questions. Selecting OpenAI when only the offline
        // provider is registered must not quietly fall back to it, or a person would believe
        // their answer came from a cloud model when it did not.
        Assert.Null(factory.Resolve(AIProviderType.OpenAI));
    }

    [Fact]
    public void AvailableProviders_ListsEveryRegisteredProvider()
    {
        var factory = Create(
            [new FakeAIProvider(AIProviderType.Mock), new FakeAIProvider(AIProviderType.OpenAI)],
            "Mock");

        Assert.Equal(2, factory.AvailableProviders.Count);
        Assert.Contains(AIProviderType.Mock, factory.AvailableProviders);
        Assert.Contains(AIProviderType.OpenAI, factory.AvailableProviders);
    }

    [Fact]
    public void SelectedModel_ReflectsAnEditedSettingOnTheNextLookup()
    {
        var monitor = new TestOptionsMonitor<AIOptions>(new AIOptions { Provider = "Mock", Model = "first-model" });
        var factory = new AIProviderFactory(
            [new FakeAIProvider(AIProviderType.Mock)],
            monitor,
            NullLogger<AIProviderFactory>.Instance);

        Assert.Equal("first-model", factory.SelectedModel);

        monitor.Set(new AIOptions { Provider = "Mock", Model = "second-model" });

        // A changed setting has to take effect without a restart, or the page would appear to
        // save something and then keep using the old value.
        Assert.Equal("second-model", factory.SelectedModel);
    }

    [Fact]
    public void SelectedProvider_FollowsAnEditedSettingOnTheNextLookup()
    {
        var monitor = new TestOptionsMonitor<AIOptions>(new AIOptions { Provider = "Mock" });
        var factory = new AIProviderFactory(
            [
                new FakeAIProvider(AIProviderType.Mock),
                new FakeAIProvider(AIProviderType.OpenAI),
            ],
            monitor,
            NullLogger<AIProviderFactory>.Instance);

        Assert.Equal(AIProviderType.Mock, factory.SelectedProvider);

        monitor.Set(new AIOptions { Provider = "OpenAI" });

        Assert.Equal(AIProviderType.OpenAI, factory.SelectedProvider);
    }

    [Fact]
    public void SelectedModel_WithAnEmptyValue_ReportsNothing()
    {
        var factory = Create([new FakeAIProvider(AIProviderType.Mock)], "Mock", model: "  ");

        Assert.Null(factory.SelectedModel);
    }

    private static AIProviderFactory Create(
        IReadOnlyCollection<IAIProvider> providers,
        string provider,
        string model = "test-model")
    {
        var options = new TestOptionsMonitor<AIOptions>(
            new AIOptions { Provider = provider, Model = model });

        return new AIProviderFactory(providers, options, NullLogger<AIProviderFactory>.Instance);
    }
}
