using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Voice.Text;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Time;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.SystemInformation;

/// <summary>
/// Answers "what time is it" and "what's today's date" from the local clock.
/// <para>
/// These are answered locally, without the AI service. Routing a clock question through a
/// model would add a network round trip, send the question off the machine, and still be less
/// accurate than the computer's own clock.
/// </para>
/// </summary>
public sealed class TimeAndDateVoiceHandler : VoiceHandlerBase
{
    private static readonly AssistantIntent[] Supported =
        [AssistantIntent.GetTime, AssistantIntent.GetDate];

    private readonly IDateTimeProvider _time;

    public TimeAndDateVoiceHandler(
        IDateTimeProvider time,
        IPermissionService permissions,
        ILogger<TimeAndDateVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(time);

        _time = time;
    }

    /// <inheritdoc />
    public override IReadOnlyCollection<AssistantIntent> Intents => Supported;

    /// <inheritdoc />
    public override Task<VoiceCommandResult> ExecuteAsync(
        VoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _time.UtcNow.ToLocalTime();
        var response = command.Intent == AssistantIntent.GetTime
            ? $"It's {ResponseTextFormatter.FormatTime(now)}."
            : $"Today is {ResponseTextFormatter.FormatDate(now)}.";

        return Task.FromResult(VoiceCommandResult.Success(
            command,
            response,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["localTime"] = now.ToString("O", CultureInfo.InvariantCulture)
            }));
    }
}
