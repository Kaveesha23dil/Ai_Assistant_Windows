using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Configuration;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Settings.Commands.SaveUserSettings;

/// <summary>
/// Saves the settings a person changed, after checking that they are ones the application
/// can actually honour.
/// <para>
/// The check matters more here than in most save paths. These values are read back as
/// configuration on the next launch, so a value that is merely well-formed but meaningless
/// would quietly disable a feature or, worse, disable a safety switch that the person
/// believed was on.
/// </para>
/// </summary>
public sealed class SaveUserSettingsHandler
{
    /// <summary>
    /// The themes the application offers. A saved theme outside this set is rejected instead
    /// of written, because nothing would honour it and the person would see their choice
    /// silently revert.
    /// </summary>
    private static readonly string[] SupportedThemes = ["System", "Light", "Dark"];

    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<SaveUserSettingsHandler> _logger;

    public SaveUserSettingsHandler(
        ISettingsStore settingsStore,
        ILogger<SaveUserSettingsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(logger);

        _settingsStore = settingsStore;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var rejection = Validate(settings);
        if (rejection is not null)
        {
            _logger.LogWarning("Settings were not saved. {Reason}", rejection);
            return Result.Failure(rejection);
        }

        try
        {
            var saved = await _settingsStore
                .SaveAsync(settings, cancellationToken)
                .ConfigureAwait(false);

            if (saved.IsFailure)
            {
                _logger.LogError("Saving settings failed. {Reason}", saved.ErrorMessage);
                return saved;
            }

            _logger.LogInformation("Settings saved to {Location}.", _settingsStore.Location);
            return saved;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Saving settings was cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Saving settings failed.");
            throw;
        }
    }

    /// <summary>
    /// Returns the reason the settings cannot be saved, or <c>null</c> when they are fine.
    /// </summary>
    private static string? Validate(UserSettings settings)
    {
        // The sections are settable, so a document assembled elsewhere could arrive without
        // one. A missing section is a broken document, not a setting to be saved.
        if (settings.Privacy is null || settings.Voice is null || settings.UI is null || settings.Features is null)
        {
            return "These settings are incomplete. Nothing was saved.";
        }

        if (!SupportedThemes.Contains(settings.UI.Theme, StringComparer.OrdinalIgnoreCase))
        {
            return $"Choose a theme from {string.Join(", ", SupportedThemes)}.";
        }

        return null;
    }
}
