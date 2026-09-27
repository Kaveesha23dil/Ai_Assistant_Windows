using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.App.ViewModels;
using WindowsAIAssistant.App.Services;
using WindowsAIAssistant.App.Views;
using WindowsAIAssistant.App.Views.Pages;
using WindowsAIAssistant.Application.Voice;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

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
        services.AddSingleton<UserSettingsService>();

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

    /// <summary>
    /// Translates the bound <c>Voice</c> configuration into the Application layer's policy.
    /// <para>
    /// The Application project cannot do this itself: it describes what the assistant may do but
    /// deliberately has no dependency on Infrastructure, where <c>VoiceOptions</c> lives. This
    /// is the one place in the solution that sees both, so the mapping is written once here
    /// rather than repeated by every host.
    /// </para>
    /// <para>
    /// The policy is a singleton, so it is read once when the pipeline is first built. A
    /// command already in flight therefore keeps the behaviour it started with rather than
    /// changing halfway through because a setting was edited. Consent is a different matter
    /// and is re-read per operation by the permission service, so revoking microphone access
    /// takes effect on the very next request. When the person saves a change, the policy is
    /// handed a new set of values as a whole, which is the one moment a running pipeline is
    /// allowed to pick up new decisions.
    /// </para>
    /// </summary>
    public static IServiceCollection AddVoiceConfiguration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp =>
        {
            var policy = new VoiceIntentPolicy();
            policy.Update(CreatePolicyValues(sp.GetRequiredService<IOptions<VoiceOptions>>().Value));

            return policy;
        });

        return services;
    }

    /// <summary>
    /// Maps the bound voice configuration onto the Application layer's policy values. Written
    /// once here because this is the only place in the solution that sees both types, and
    /// reused when a save has to be applied to a running session.
    /// </summary>
    public static VoiceIntentPolicyValues CreatePolicyValues(VoiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new VoiceIntentPolicyValues
        {
            Enabled = options.Enabled,
            SpeakResponses = options.SpeakResponses,
            EnableContinuousListening = options.ContinuousListening,
            Language = options.Language,
            MinimumCommandConfidence = options.MinimumCommandConfidence,
            ConfirmLowConfidenceCommands = options.ConfirmLowConfidenceCommands,
            MaximumSpokenResponseLength = options.MaximumSpokenResponseLength,
        };
    }
}
