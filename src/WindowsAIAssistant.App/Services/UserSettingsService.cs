using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Application.Settings.Commands.SaveUserSettings;
using WindowsAIAssistant.Application.Voice;
using WindowsAIAssistant.Core.Abstractions.Configuration;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.App.Services;

/// <summary>
/// Applies a settings change to everything that reads one.
/// <para>
/// Saving a file is only half the job. Until the running application is told to look again,
/// the values a person just saved would sit on disk while the session carried on with the old
/// ones, and the Settings page would appear not to have worked. This service is the one place
/// that closes that gap: it writes the settings, reloads configuration so the options monitors
/// see them, and hands the voice policy its new decisions.
/// </para>
/// <para>
/// It lives in the App project because that is the only place holding every piece: the save
/// handler, the configuration root, and the option types the voice policy is built from.
/// </para>
/// </summary>
public sealed class UserSettingsService
{
    private readonly SaveUserSettingsHandler _saveSettings;
    private readonly ISettingsStore _settingsStore;
    private readonly IConfigurationRoot? _configuration;
    private readonly IOptionsMonitor<VoiceOptions> _voiceOptions;
    private readonly VoiceIntentPolicy _voicePolicy;
    private readonly ILogger<UserSettingsService> _logger;

    public UserSettingsService(
        SaveUserSettingsHandler saveSettings,
        ISettingsStore settingsStore,
        IConfiguration configuration,
        IOptionsMonitor<VoiceOptions> voiceOptions,
        VoiceIntentPolicy voicePolicy,
        ILogger<UserSettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(saveSettings);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(voiceOptions);
        ArgumentNullException.ThrowIfNull(voicePolicy);
        ArgumentNullException.ThrowIfNull(logger);

        _saveSettings = saveSettings;
        _settingsStore = settingsStore;

        // Reloading needs the root. The default host supplies one, and a save works without
        // this being set, so a host that does not is a reason to report the save as needing a
        // restart rather than a reason to refuse to start.
        _configuration = configuration as IConfigurationRoot;
        if (_configuration is null)
        {
            logger.LogWarning(
                "Configuration is not reloadable, so a saved change applies on the next start instead.");
        }

        _voiceOptions = voiceOptions;
        _voicePolicy = voicePolicy;
        _logger = logger;
    }

    /// <summary>
    /// Gets a description of where the settings are kept, shown to the person so they know
    /// which file to look at or delete.
    /// </summary>
    public string Location => _settingsStore.Location;

    /// <summary>
    /// Saves the settings and applies them to the running session.
    /// </summary>
    public async Task<Result> SaveAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var saved = await _saveSettings.HandleAsync(settings, cancellationToken).ConfigureAwait(true);
        if (saved.IsFailure)
        {
            return saved;
        }

        if (_configuration is null)
        {
            return Result.Success(
                "Settings were saved. Restart the application to apply them.");
        }

        try
        {
            // Reloading re-reads every source, including the settings file that was just
            // written, which is what pushes the change into the options monitors.
            _configuration.Reload();

            // The monitors have re-bound by now, so this reads the saved values rather than
            // the ones from startup.
            _voicePolicy.Update(DependencyInjection.CreatePolicyValues(_voiceOptions.CurrentValue));

            _logger.LogInformation("Saved settings applied to the running session.");
        }
        catch (Exception exception)
        {
            // The settings are safely on disk at this point, so the save has not failed. The
            // running session simply did not pick them up, and saying so is more useful than
            // reporting the save as lost.
            _logger.LogError(exception, "Settings were saved but could not be applied to this session.");
            return Result.Failure(
                "Settings were saved, but this session is still using the previous values. Restart to apply them.");
        }

        return Result.Success();
    }
}
