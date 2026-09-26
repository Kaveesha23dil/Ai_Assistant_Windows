using Microsoft.Extensions.DependencyInjection;
using WindowsAIAssistant.App.ViewModels;
using WindowsAIAssistant.App.Views;
using WindowsAIAssistant.App.Views.Pages;

namespace WindowsAIAssistant.App;

/// <summary>
/// Registers the presentation layer. ViewModels are resolved by the shell, and pages are
/// registered transiently so each navigation visit gets a fresh page instance.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAppShell(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddTransient<HomeViewModel>();
        services.AddTransient<ChatViewModel>();
        services.AddTransient<FilesViewModel>();
        services.AddTransient<AutomationsViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddTransient<HomePage>();
        services.AddTransient<ChatPage>();
        services.AddTransient<FilesPage>();
        services.AddTransient<AutomationsPage>();
        services.AddTransient<SettingsPage>();

        return services;
    }
}
