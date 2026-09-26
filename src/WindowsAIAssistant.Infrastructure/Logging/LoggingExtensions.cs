using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WindowsAIAssistant.Infrastructure.Logging;

/// <summary>
/// Centralizes the logging pipeline for the desktop host so additional providers
/// (file, Windows Event Log, OpenTelemetry, Application Insights) can be added in one place.
/// </summary>
public static class LoggingExtensions
{
    private const string LoggingSectionName = "Logging";
    private const string DevelopmentEnvironmentName = "Development";

    /// <summary>
    /// Configures the debug provider, applies <c>Logging:LogLevel</c> from configuration,
    /// and applies a production-safe minimum level.
    /// </summary>
    public static ILoggingBuilder AddApplicationLogging(
        this ILoggingBuilder builder,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        builder.ClearProviders();
        builder.AddDebug();
        builder.AddConfiguration(configuration.GetSection(LoggingSectionName));
        builder.SetMinimumLevel(IsDevelopment(environmentName) ? LogLevel.Debug : LogLevel.Information);

        return builder;
    }

    private static bool IsDevelopment(string environmentName) =>
        string.Equals(environmentName, DevelopmentEnvironmentName, StringComparison.OrdinalIgnoreCase);
}
