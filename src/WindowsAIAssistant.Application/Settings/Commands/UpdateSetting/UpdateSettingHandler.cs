using WindowsAIAssistant.Application.Common.Validation;
using WindowsAIAssistant.Core.Abstractions.Storage;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Application.Settings.Commands.UpdateSetting;

public sealed class UpdateSettingHandler<T>
{
    private readonly ISettingsStorage _settingsStorage;

    public UpdateSettingHandler(ISettingsStorage settingsStorage)
    {
        ArgumentNullException.ThrowIfNull(settingsStorage);
        _settingsStorage = settingsStorage;
    }

    public async Task<Result> HandleAsync(
        UpdateSettingCommand<T> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var key = ValidationHelper.RequireText(
            command.Key,
            nameof(command.Key),
            "Setting key cannot be empty.");

        await _settingsStorage
            .SetAsync(key, command.Value, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success();
    }
}
