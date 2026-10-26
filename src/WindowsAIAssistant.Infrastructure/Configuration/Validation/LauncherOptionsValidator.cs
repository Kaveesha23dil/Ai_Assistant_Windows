using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Models.Launcher;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Configuration.Validation;

/// <summary>
/// Rejects launcher settings that the running application could not honour.
/// </summary>
public sealed class LauncherOptionsValidator : IValidateOptions<LauncherOptions>
{
    public ValidateOptionsResult Validate(string? name, LauncherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Enabled && !HotkeyGesture.TryParse(options.Hotkey, out _, out var error))
        {
            return ValidateOptionsResult.Fail($"Launcher:Hotkey is invalid. {error}");
        }

        if (options.MaximumResults is < 1 or > 50)
        {
            return ValidateOptionsResult.Fail("Launcher:MaximumResults must be from 1 to 50.");
        }

        if (options.DebounceMilliseconds is < 50 or > 1000)
        {
            return ValidateOptionsResult.Fail("Launcher:DebounceMilliseconds must be from 50 to 1000.");
        }

        if (options.ProviderTimeoutMilliseconds is < 100 or > 5000)
        {
            return ValidateOptionsResult.Fail("Launcher:ProviderTimeoutMilliseconds must be from 100 to 5000.");
        }

        return ValidateOptionsResult.Success;
    }
}
