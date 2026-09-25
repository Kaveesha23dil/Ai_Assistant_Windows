using WindowsAIAssistant.Application.Common.Validation;
using WindowsAIAssistant.Core.Abstractions.Storage;

namespace WindowsAIAssistant.Application.Settings.Queries.GetSetting;

public sealed class GetSettingHandler<T>
{
    private readonly ISettingsStorage _settingsStorage;

    public GetSettingHandler(ISettingsStorage settingsStorage)
    {
        ArgumentNullException.ThrowIfNull(settingsStorage);
        _settingsStorage = settingsStorage;
    }

    public async Task<T?> HandleAsync(
        GetSettingQuery<T> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var key = ValidationHelper.RequireText(
            query.Key,
            nameof(query.Key),
            "Setting key cannot be empty.");

        return await _settingsStorage
            .GetAsync<T>(key, cancellationToken)
            .ConfigureAwait(false);
    }
}
