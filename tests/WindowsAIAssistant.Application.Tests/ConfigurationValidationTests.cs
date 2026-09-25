using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Infrastructure.Configuration;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Application.Tests;

public sealed class ConfigurationValidationTests
{
    [Theory]
    [InlineData("AI:Provider", "", "AI:Provider must not be empty.")]
    [InlineData("AI:Model", "", "AI:Model must not be empty for the configured provider.")]
    [InlineData("AI:Temperature", "5", "AI:Temperature must be between 0.0 and 2.0.")]
    [InlineData("AI:MaxOutputTokens", "0", "AI:MaxOutputTokens must be greater than zero.")]
    [InlineData("AI:RequestTimeoutSeconds", "0", "AI:RequestTimeoutSeconds must be between 1 and 600.")]
    [InlineData("AI:RequestTimeoutSeconds", "601", "AI:RequestTimeoutSeconds must be between 1 and 600.")]
    public void AddApplicationConfiguration_WithInvalidAIValue_FailsWithMeaningfulMessage(
        string key,
        string value,
        string expectedMessage)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [key] = value
        });
        var services = new ServiceCollection();
        services.AddApplicationConfiguration(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AIOptions>>().Value);

        Assert.Contains(expectedMessage, exception.Message);
    }

    [Theory]
    [InlineData("Application:Name", "", "Application:Name must not be empty.")]
    [InlineData("Application:Environment", "Staging", "Application:Environment must be either Development or Production.")]
    public void AddApplicationConfiguration_WithInvalidApplicationValue_FailsWithMeaningfulMessage(
        string key,
        string value,
        string expectedMessage)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [key] = value
        });
        var services = new ServiceCollection();
        services.AddApplicationConfiguration(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ApplicationOptions>>().Value);

        Assert.Contains(expectedMessage, exception.Message);
    }

    [Fact]
    public async Task Host_WithValidConfiguration_StartsSuccessfully()
    {
        using var host = CreateHost(new Dictionary<string, string?>
        {
            ["Application:Name"] = "Test Assistant",
            ["Application:Environment"] = "Development",
            ["AI:Provider"] = "Mock",
            ["AI:Model"] = "test-model"
        });

        await host.StartAsync();

        Assert.Equal(
            "Test Assistant",
            host.Services.GetRequiredService<IOptions<ApplicationOptions>>().Value.Name);
        await host.StopAsync();
    }

    [Fact]
    public async Task Host_WithInvalidConfiguration_FailsDuringStartup()
    {
        using var host = CreateHost(new Dictionary<string, string?>
        {
            ["AI:Provider"] = ""
        });

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("AI:Provider must not be empty.", exception.Message);
    }

    [Fact]
    public void AddApplicationConfiguration_WithAIChatEnabledAndNoProvider_FailsValidation()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Features:EnableAIChat"] = "true",
            ["AI:Provider"] = ""
        });
        var services = new ServiceCollection();
        services.AddApplicationConfiguration(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AIOptions>>().Value);

        Assert.Contains("AI:Provider must not be empty.", exception.Message);
    }

    private static IConfiguration BuildConfiguration(IEnumerable<KeyValuePair<string, string?>> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IHost CreateHost(IEnumerable<KeyValuePair<string, string?>> values)
    {
        return new HostBuilder()
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(values))
            .ConfigureServices((context, services) =>
                services.AddApplicationConfiguration(context.Configuration))
            .Build();
    }
}
