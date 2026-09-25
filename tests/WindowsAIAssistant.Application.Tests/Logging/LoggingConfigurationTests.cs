using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Infrastructure.Logging;

namespace WindowsAIAssistant.Application.Tests.Logging;

public sealed class LoggingConfigurationTests
{
    [Fact]
    public void AddApplicationLogging_InDevelopment_UsesDebugMinimumAndDebugProvider()
    {
        var (options, providers) = Configure(
            new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Debug",
                ["Logging:LogLevel:Microsoft"] = "Information"
            },
            "Development");

        Assert.Equal(LogLevel.Debug, options.MinLevel);
        Assert.Contains(providers, provider => provider.GetType().Name == "DebugLoggerProvider");
    }

    [Fact]
    public void AddApplicationLogging_InProduction_UsesInformationMinimumSoItIsNotVerbose()
    {
        var (options, _) = Configure(
            new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Information",
                ["Logging:LogLevel:Microsoft"] = "Warning"
            },
            "Production");

        Assert.Equal(LogLevel.Information, options.MinLevel);
    }

    [Fact]
    public void AddApplicationLogging_AppliesMicrosoftCategoryFilterFromConfiguration()
    {
        var (options, _) = Configure(
            new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Information",
                ["Logging:LogLevel:Microsoft"] = "Warning"
            },
            "Production");

        var microsoftRule = Assert.Single(options.Rules, rule => rule.CategoryName == "Microsoft");
        Assert.Equal(LogLevel.Warning, microsoftRule.LogLevel);
    }

    [Fact]
    public void AddApplicationLogging_WithNullBuilder_ThrowsArgumentNullException()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(
            () => LoggingExtensions.AddApplicationLogging(null!, configuration, "Production"));
    }

    private static (LoggerFilterOptions Options, IReadOnlyList<ILoggerProvider> Providers) Configure(
        IDictionary<string, string?> values,
        string environmentName)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddApplicationLogging(configuration, environmentName));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LoggerFilterOptions>>().Value;
        var providers = provider.GetServices<ILoggerProvider>().ToArray();
        return (options, providers);
    }
}
