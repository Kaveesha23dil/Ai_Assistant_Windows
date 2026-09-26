using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using WindowsAIAssistant.Infrastructure.Configuration.Validation;

namespace WindowsAIAssistant.Infrastructure.Configuration;

public static class ConfigurationExtensions
{
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
