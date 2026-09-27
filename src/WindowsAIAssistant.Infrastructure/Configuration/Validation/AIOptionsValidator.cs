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

        if (options.MaxConversationMessages < 2)
        {
            // Two is the floor that still makes a conversation coherent: a question and the
            // answer to it. One would mean the model never saw what it was replying to.
            failures.Add("AI:MaxConversationMessages must be at least 2.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKeyEnvironmentVariable))
        {
            failures.Add("AI:ApiKeyEnvironmentVariable must name an environment variable.");
        }

        // An unrecognised provider name is deliberately not a failure here. The factory reports
        // it at the moment a request is made, with a sentence the person can act on, which is
        // more use than refusing to start the application.
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
