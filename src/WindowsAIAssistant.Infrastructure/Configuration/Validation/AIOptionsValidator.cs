using Microsoft.Extensions.Options;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Configuration.Validation;

public sealed class AIOptionsValidator : IValidateOptions<AIOptions>
{
    public ValidateOptionsResult Validate(string? name, AIOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            failures.Add("AI:Provider must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            failures.Add("AI:Model must not be empty for the configured provider.");
        }

        if (double.IsNaN(options.Temperature) || options.Temperature is < 0.0 or > 2.0)
        {
            failures.Add("AI:Temperature must be between 0.0 and 2.0.");
        }

        if (options.MaxOutputTokens <= 0)
        {
            failures.Add("AI:MaxOutputTokens must be greater than zero.");
        }

        if (options.RequestTimeoutSeconds is < 1 or > 600)
        {
            failures.Add("AI:RequestTimeoutSeconds must be between 1 and 600.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
