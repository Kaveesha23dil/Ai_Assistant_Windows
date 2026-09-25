using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Abstractions.Storage;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Infrastructure.Configuration;
using WindowsAIAssistant.Infrastructure.Development;

namespace WindowsAIAssistant.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddApplicationConfiguration(configuration);
        services.AddSingleton<IAIService, MockAIService>();
        services.AddSingleton<IWindowsSystemService, MockWindowsSystemService>();
        services.AddSingleton<IApplicationLauncherService, MockApplicationLauncherService>();
        services.AddSingleton<IFileSearchService, MockFileSearchService>();
        services.AddSingleton<IClipboardService, MockClipboardService>();
        services.AddSingleton<ISettingsStorage, InMemorySettingsStorage>();

        return services;
    }
}
