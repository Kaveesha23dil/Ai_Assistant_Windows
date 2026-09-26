using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.OpenFolder;

/// <summary>
/// Handles "open downloads" and the other well-known user folders.
/// <para>
/// The folder is resolved through <see cref="IKnownFolderService"/> and handed to the shell
/// as a URI. No path is ever assembled from a user name, so redirected and localized profile
/// folders still resolve correctly.
/// </para>
/// </summary>
public sealed class OpenFolderVoiceHandler : VoiceHandlerBase
{
    private static readonly AssistantIntent[] Supported = [AssistantIntent.OpenFolder];

    private static readonly IReadOnlyDictionary<string, KnownFolderKind> Folders =
        new Dictionary<string, KnownFolderKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["download"] = KnownFolderKind.Downloads,
            ["downloads"] = KnownFolderKind.Downloads,
            ["document"] = KnownFolderKind.Documents,
            ["documents"] = KnownFolderKind.Documents,
            ["desktop"] = KnownFolderKind.Desktop,
            ["picture"] = KnownFolderKind.Pictures,
            ["pictures"] = KnownFolderKind.Pictures,
            ["photo"] = KnownFolderKind.Pictures,
            ["photos"] = KnownFolderKind.Pictures,
            ["music"] = KnownFolderKind.Music,
            ["video"] = KnownFolderKind.Videos,
            ["videos"] = KnownFolderKind.Videos
        };

    private readonly IKnownFolderService _knownFolders;
    private readonly IUriLauncherService _uriLauncher;

    public OpenFolderVoiceHandler(
        IKnownFolderService knownFolders,
        IUriLauncherService uriLauncher,
        IPermissionService permissions,
        ILogger<OpenFolderVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(knownFolders);
        ArgumentNullException.ThrowIfNull(uriLauncher);

        _knownFolders = knownFolders;
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

        if (!TryGetRequiredParameter(
                command,
                VoiceCommand.FolderParameter,
                out var spoken,
                out var failure))
        {
            return failure;
        }

        if (!Folders.TryGetValue(spoken, out var folder))
        {
            return Invalid(command, $"I don't know a folder called {spoken}.");
        }

        try
        {
            var path = await _knownFolders.GetPathAsync(folder, cancellationToken).ConfigureAwait(false);
            if (path.IsFailure || string.IsNullOrWhiteSpace(path.Value))
            {
                return Unavailable(command, $"I couldn't find your {spoken} folder.");
            }

            if (!Uri.TryCreate(path.Value, UriKind.Absolute, out var uri))
            {
                return Unavailable(command, $"I couldn't open your {spoken} folder.");
            }

            var opened = await _uriLauncher.OpenAsync(uri, cancellationToken).ConfigureAwait(false);
            return opened.IsSuccess
                ? VoiceCommandResult.Success(command, $"Opening {spoken}.")
                : Unavailable(command, $"I couldn't open your {spoken} folder.");
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
}
