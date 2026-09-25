using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Application.AI.Commands.SendMessage;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Infrastructure;
using WindowsAIAssistant.Infrastructure.Configuration;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using WindowsAIAssistant.Infrastructure.Development;

namespace WindowsAIAssistant.Application.Tests;

public sealed class ConfigurationBindingTests
{
    [Fact]
    public void AddApplicationConfiguration_WithCompleteValues_BindsAllOptions()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Application:Name"] = "Test Assistant",
            ["Application:Environment"] = "Development",
            ["Application:EnableDiagnostics"] = "true",
            ["AI:Provider"] = "Local",
            ["AI:Model"] = "test-model",
            ["AI:Temperature"] = "1.25",
            ["AI:MaxOutputTokens"] = "4096",
            ["AI:RequestTimeoutSeconds"] = "120",
            ["AI:UseStreaming"] = "true",
            ["Features:EnableAIChat"] = "false",
            ["Features:EnableFileSearch"] = "false",
            ["Features:EnableClipboard"] = "false",
            ["Features:EnableSystemInformation"] = "false",
            ["Features:EnableAutomation"] = "true",
            ["Features:EnableVoice"] = "true",
            ["Features:EnableScreenAI"] = "true",
            ["Features:EnableLocalAI"] = "true",
            ["Privacy:AllowCloudAI"] = "true",
            ["Privacy:AllowTelemetry"] = "true",
            ["Privacy:AllowClipboardProcessing"] = "true",
            ["Privacy:AllowFileIndexing"] = "true",
            ["Privacy:AllowScreenAnalysis"] = "true",
            ["Privacy:StoreConversationHistory"] = "true",
            ["UI:Theme"] = "Dark",
            ["UI:DefaultPage"] = "Settings",
            ["UI:ShowSystemTrayIcon"] = "false",
            ["UI:LaunchMinimized"] = "true"
        });
        var services = new ServiceCollection();
        using var provider = services.AddApplicationConfiguration(configuration).BuildServiceProvider();

        var application = provider.GetRequiredService<IOptions<ApplicationOptions>>().Value;
        Assert.Equal("Test Assistant", application.Name);
        Assert.Equal("Development", application.Environment);
        Assert.True(application.EnableDiagnostics);

        var ai = provider.GetRequiredService<IOptions<AIOptions>>().Value;
        Assert.Equal("Local", ai.Provider);
        Assert.Equal("test-model", ai.Model);
        Assert.Equal(1.25, ai.Temperature);
        Assert.Equal(4096, ai.MaxOutputTokens);
        Assert.Equal(120, ai.RequestTimeoutSeconds);
        Assert.True(ai.UseStreaming);

        var features = provider.GetRequiredService<IOptions<FeatureOptions>>().Value;
        Assert.False(features.EnableAIChat);
        Assert.False(features.EnableFileSearch);
        Assert.False(features.EnableClipboard);
        Assert.False(features.EnableSystemInformation);
        Assert.True(features.EnableAutomation);
        Assert.True(features.EnableVoice);
        Assert.True(features.EnableScreenAI);
        Assert.True(features.EnableLocalAI);

        var privacy = provider.GetRequiredService<IOptions<PrivacyOptions>>().Value;
        Assert.True(privacy.AllowCloudAI);
        Assert.True(privacy.AllowTelemetry);
        Assert.True(privacy.AllowClipboardProcessing);
        Assert.True(privacy.AllowFileIndexing);
        Assert.True(privacy.AllowScreenAnalysis);
        Assert.True(privacy.StoreConversationHistory);

        var ui = provider.GetRequiredService<IOptions<UIOptions>>().Value;
        Assert.Equal("Dark", ui.Theme);
        Assert.Equal("Settings", ui.DefaultPage);
        Assert.False(ui.ShowSystemTrayIcon);
        Assert.True(ui.LaunchMinimized);
    }

    [Fact]
    public void AddApplicationAndInfrastructure_WithValidConfiguration_ResolvesApplicationServices()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AI:Provider"] = "Mock",
            ["AI:Model"] = "test-model"
        });
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<MockAIService>(provider.GetRequiredService<IAIService>());
        Assert.NotNull(provider.GetRequiredService<SendMessageHandler>());
        Assert.Equal("test-model", provider.GetRequiredService<IOptions<AIOptions>>().Value.Model);
    }

    [Fact]
    public void AddApplicationConfiguration_WithLaterSource_UsesLastValue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Provider"] = "Mock",
                ["AI:Model"] = "base-model"
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Provider"] = "Local",
                ["AI:Model"] = "override-model"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddApplicationConfiguration(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AIOptions>>().Value;

        Assert.Equal("Local", options.Provider);
        Assert.Equal("override-model", options.Model);
    }

    [Fact]
    public void AddApplicationConfiguration_WithMissingPrivacySection_UsesSafeDefaults()
    {
        var configuration = BuildConfiguration([]);
        var services = new ServiceCollection();
        services.AddApplicationConfiguration(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PrivacyOptions>>().Value;

        Assert.False(options.AllowCloudAI);
        Assert.False(options.AllowTelemetry);
        Assert.False(options.AllowClipboardProcessing);
        Assert.False(options.AllowFileIndexing);
        Assert.False(options.AllowScreenAnalysis);
        Assert.False(options.StoreConversationHistory);
    }

    [Fact]
    public void AddApplicationConfiguration_WithLocalAIEnabledAndCloudDenied_UsesValidConfiguration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Features:EnableLocalAI"] = "true",
            ["Privacy:AllowCloudAI"] = "false"
        });
        var services = new ServiceCollection();
        services.AddApplicationConfiguration(configuration);
        using var provider = services.BuildServiceProvider();

        var features = provider.GetRequiredService<IOptions<FeatureOptions>>().Value;
        var privacy = provider.GetRequiredService<IOptions<PrivacyOptions>>().Value;

        Assert.True(features.EnableLocalAI);
        Assert.False(privacy.AllowCloudAI);
    }

    private static IConfiguration BuildConfiguration(IEnumerable<KeyValuePair<string, string?>> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
