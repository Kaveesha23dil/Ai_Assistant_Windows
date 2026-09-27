using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Configuration;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using WindowsAIAssistant.Infrastructure.Configuration.Persistence;
using WindowsAIAssistant.Infrastructure.Configuration.Validation;

namespace WindowsAIAssistant.Infrastructure.Configuration;

public static class ConfigurationExtensions
{
    /// <summary>
    /// Registers the place the person's own settings are kept.
    /// <para>
    /// The file itself is added to configuration by <see cref="AddUserSettingsFile"/>, which has
    /// to be called from the host's own configuration callback. That split is deliberate: the
    /// configuration object a host hands to its service registrations can be read-only, so
    /// trying to add a source from there fails at startup on some hosts and works on others.
    /// </para>
    /// </summary>
    /// <param name="services">The services to add the store to.</param>
    /// <param name="settingsPath">
    /// The file to use instead of the default location. Intended for tests.
    /// </param>
    public static IServiceCollection AddUserSettings(
        this IServiceCollection services,
        string? settingsPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var path = settingsPath ?? JsonFileSettingsStore.DefaultPath();

        services.AddSingleton<ISettingsStore>(provider =>
            new JsonFileSettingsStore(
                provider.GetRequiredService<ILogger<JsonFileSettingsStore>>(),
                path));

        return services;
    }

    /// <summary>
    /// Adds the person's settings file to configuration, so a saved choice is read on the next
    /// launch instead of the shipped default.
    /// <para>
    /// The source is added after the ones the host already registered, so a value the person
    /// chose wins over the shipped default, while a value passed on the command line still
    /// wins over both. That keeps a debugging override working without a code change.
    /// </para>
    /// <para>
    /// The file is not watched. A save reloads configuration explicitly once the write has
    /// completed, which avoids reacting to a half-written file and makes the moment a change
    /// takes effect a deliberate one.
    /// </para>
    /// </summary>
    /// <param name="builder">The host's configuration builder.</param>
    /// <param name="settingsPath">
    /// The file to use instead of the default location. Intended for tests.
    /// </param>
    public static IConfigurationBuilder AddUserSettingsFile(
        this IConfigurationBuilder builder,
        string? settingsPath = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var path = settingsPath ?? JsonFileSettingsStore.DefaultPath();

        // Optional, because on a first run there is nothing saved yet and that is not an error.
        builder.AddJsonFile(path, optional: true, reloadOnChange: false);

        return builder;
    }

    public static IServiceCollection AddApplicationConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<ApplicationOptions>, ApplicationOptionsValidator>();
        services.AddSingleton<IValidateOptions<AIOptions>, AIOptionsValidator>();
        services.AddSingleton<IValidateOptions<VoiceOptions>, VoiceOptionsValidator>();

        services.AddOptions<ApplicationOptions>()
            .Bind(configuration.GetSection(ApplicationOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<AIOptions>()
            .Bind(configuration.GetSection(AIOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<FeatureOptions>()
            .Bind(configuration.GetSection(FeatureOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<PrivacyOptions>()
            .Bind(configuration.GetSection(PrivacyOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<VoiceOptions>()
            .Bind(configuration.GetSection(VoiceOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<UIOptions>()
            .Bind(configuration.GetSection(UIOptions.SectionName))
            .ValidateOnStart();

        return services;
    }
}
