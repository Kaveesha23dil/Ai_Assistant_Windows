using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.OpenFolder;

/// <summary>
/// Handles "open Bluetooth settings" and the other supported Windows Settings pages.
/// <para>
/// The spoken page name is resolved by <see cref="IWindowsSettingsService"/> against a fixed
/// table of known addresses. The handler never builds a URI from the transcript, so a crafted
/// sentence cannot smuggle a different scheme past the allow list.
/// </para>
/// </summary>
public sealed class OpenSettingsVoiceHandler : VoiceHandlerBase
{
    private static readonly AssistantIntent[] Supported = [AssistantIntent.OpenSettings];

    private readonly IWindowsSettingsService _settings;
    private readonly IUriLauncherService _uriLauncher;

    public OpenSettingsVoiceHandler(
        IWindowsSettingsService settings,
        IUriLauncherService uriLauncher,
        IPermissionService permissions,
        ILogger<OpenSettingsVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(uriLauncher);

        _settings = settings;
        _uriLauncher = uriLauncher;
    }

    /// <inheritdoc />
    public override IReadOnlyCollection<AssistantIntent> Intents => Supported;

    /// <inheritdoc />
    public override async Task<VoiceCommandResult> ExecuteAsync(
        VoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Permissions.IsGranted(PermissionCapability.ApplicationLaunch))
        {
            return Denied(command, PermissionCapability.ApplicationLaunch);
        }

        var page = command.GetParameter(VoiceCommand.SettingParameter) ?? DefaultPage;

        var resolved = _settings.ResolvePageUri(page);
        if (resolved.IsFailure || resolved.Value is null)
        {
            return Invalid(command, $"I don't know a settings page called {page}.");
        }

        try
        {
            var opened = await _uriLauncher.OpenAsync(resolved.Value, cancellationToken).ConfigureAwait(false);
            return opened.IsSuccess
                ? VoiceCommandResult.Success(command, $"Opening {page} settings.")
                : Unavailable(command, $"I couldn't open {page} settings.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failed(command, exception);
        }
    }

    private const string DefaultPage = "settings";
}
