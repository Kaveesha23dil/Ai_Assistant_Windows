using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Storage;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Time;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Abstractions.Web;
using WindowsAIAssistant.Infrastructure.Configuration;
using WindowsAIAssistant.Infrastructure.Development;
using WindowsAIAssistant.Infrastructure.Voice;
using WindowsAIAssistant.Infrastructure.Web;
using WindowsAIAssistant.Infrastructure.Windows;

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
        services.AddWindowsServices();
        services.AddWebSearchProviders();
        services.AddVoiceServices();

        services.AddSingleton<IAIService, MockAIService>();

        // File search and settings storage are still the development implementations. The voice
        // layer calls them through the same abstractions, so swapping in the real services later
        // needs no change to any command handler.
        services.AddSingleton<IFileSearchService, MockFileSearchService>();
        services.AddSingleton<ISettingsStorage, InMemorySettingsStorage>();

        return services;
    }

    /// <summary>
    /// Replaces the services that touch the real machine with the development
    /// implementations.
    /// <para>
    /// The production composition root deliberately wires up the real Windows services, so a
    /// test that wants predictable data has to ask for this explicitly rather than receiving
    /// fake values by accident. Call it after <see cref="AddInfrastructure"/>: the last
    /// registration for a service is the one that resolves.
    /// </para>
    /// </summary>
    public static IServiceCollection AddDevelopmentServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IWindowsSystemService, MockWindowsSystemService>();
        services.AddSingleton<IClipboardService, MockClipboardService>();
        services.AddSingleton<IApplicationLauncherService, MockApplicationLauncherService>();

        return services;
    }

    /// <summary>
    /// Registers the platform implementations of the system services.
    /// <para>
    /// Every one of them is a singleton because each wraps a single machine-wide resource such
    /// as the audio endpoint or the Start menu index. Launching, in particular, goes through
    /// the resolver rather than being given a command line, so the only way to start a process
    /// is a name that matched the allow list.
    /// </para>
    /// </summary>
    private static void AddWindowsServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IApplicationResolver, ApplicationResolver>();
        services.AddSingleton<IApplicationLauncherService, WindowsApplicationLauncherService>();
        services.AddSingleton<IWindowsSystemService, WindowsSystemService>();
        services.AddSingleton<IClipboardService, WindowsClipboardService>();
        services.AddSingleton<IKnownFolderService, KnownFolderService>();
        services.AddSingleton<IUriLauncherService, UriLauncherService>();
        services.AddSingleton<IWindowsSettingsService, WindowsSettingsService>();
        services.AddSingleton<IDriveSpaceService, DriveSpaceService>();
        services.AddSingleton<IBatteryService, BatteryService>();
        services.AddSingleton<IVolumeService, VolumeService>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        return;
    }

    private static void AddWebSearchProviders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The voice handler resolves the provider by name, so the collection is the extension
        // point: a new site is a new registration and no handler changes.
        services.AddSingleton<IWebSearchProvider, GoogleSearchProvider>();
        services.AddSingleton<IWebSearchProvider, YouTubeSearchProvider>();
        services.AddSingleton<IWebSearchProvider, GitHubSearchProvider>();
        services.AddSingleton<IWebSearchProvider, StackOverflowSearchProvider>();

        return;
    }

    /// <summary>
    /// Registers the voice pipeline.
    /// <para>
    /// The recognition, synthesis, and control services are singletons because each owns a
    /// device the process may only hold once: two recognizers competing for the microphone, or
    /// two synthesizer sessions, would fight rather than queue. The intent recognizer is also
    /// shared because its rule table is compiled once.
    /// </para>
    /// </summary>
    private static void AddVoiceServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<ISpeechRecognitionService, SpeechRecognitionService>();
        services.AddSingleton<ISpeechSynthesisService, SpeechSynthesisService>();
        services.AddSingleton<IWakeWordService, WakeWordService>();

        return;
    }
}
