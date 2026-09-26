using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.Screenshot;

/// <summary>
/// Handles "take a screenshot".
/// <para>
/// The capture is saved to a local file and nothing else happens to it. The saved path goes
/// into the result data for the user interface, while the spoken reply deliberately contains
/// only the folder name. Sending a capture to an AI service is a separate capability with its
/// own consent switch and is not reachable from this intent.
/// </para>
/// </summary>
public sealed class ScreenshotVoiceHandler : VoiceHandlerBase
{
    private static readonly AssistantIntent[] Supported = [AssistantIntent.TakeScreenshot];

    private readonly IScreenshotService _screenshot;

    public ScreenshotVoiceHandler(
        IScreenshotService screenshot,
        IPermissionService permissions,
        ILogger<ScreenshotVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(screenshot);

        _screenshot = screenshot;
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

        if (!Permissions.IsGranted(PermissionCapability.ScreenCapture))
        {
            return Denied(command, PermissionCapability.ScreenCapture);
        }

        try
        {
            var capture = await _screenshot.CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (capture.IsFailure || capture.Value is null)
            {
                return Unavailable(command, "I couldn't take a screenshot.");
            }

            return VoiceCommandResult.Success(
                command,
                "Screenshot saved to your pictures folder.",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["filePath"] = capture.Value.FilePath,
                    ["sizeInBytes"] = capture.Value.SizeInBytes.ToString(
                        CultureInfo.InvariantCulture)
                });
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
