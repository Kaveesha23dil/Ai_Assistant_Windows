# Logging Policy

The Windows AI Assistant uses `Microsoft.Extensions.Logging` exclusively. All logging flows
through `ILogger<T>` resolved from DI. There is no static logger, and no scattered
`Console.WriteLine` or `Debug.WriteLine`.

## Levels

| Level | Used for |
| --- | --- |
| `Debug` | Low-level diagnostics such as clipboard retrieval start. |
| `Information` | Lifecycle events: app start/stop, service start/complete, cancellations. |
| `Warning` | Recoverable problems, e.g. a launch that returned a failure result. |
| `Error` | A handled failure that was mapped to a safe error. |
| `Critical` | Unhandled exceptions at the application boundary. |

## Structured logging

Always use message templates with named placeholders. Never interpolate.

```csharp
// Good
_logger.LogInformation("Environment detected: {Environment}.", environment);

// Bad - not structured, not analyzable
_logger.LogInformation($"Environment detected: {environment}.");
```

Use class-based loggers (`ILogger<SendMessageHandler>`, `ILogger<App>`) so each component owns
its category. Categories drive the `Logging:LogLevel` filters in `appsettings.json`.

## Configuration

`appsettings.json` (production defaults):

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft": "Warning",
    "Microsoft.Hosting.Lifetime": "Information"
  }
}
```

`appsettings.Development.json` raises verbosity locally:

```json
"Logging": {
  "LogLevel": {
    "Default": "Debug",
    "Microsoft": "Information"
  }
}
```

`AddApplicationLogging` (`Infrastructure/Logging/LoggingExtensions.cs`) clears the default
providers, adds the Debug provider, applies the `Logging` section, and forces a production
minimum of `Information` so a shipped build is never excessively verbose.

## Sensitive data rules

Never log:

- API keys, access tokens, passwords, authentication headers, or any secure configuration value
- full clipboard contents (or clipboard length, which has no diagnostic value)
- AI message or conversation content
- private document or personal file contents
- full file paths, unless diagnostics explicitly permit sanitized path data

Log the fact of a failure, plus non-identifying context (counts, lengths, provider type).

```csharp
// Good
_logger.LogInformation("File search completed with {ResultCount} results.", results.Count);
_logger.LogError(exception, "AI message request failed.");

// Bad
_logger.LogInformation("Clipboard content: {Text}", text);
_logger.LogError(exception, "OpenAI API Key: sk-...");
_logger.LogError(exception, "Failed to read C:\Users\John\Documents\Private\salary.pdf");
```

Prefer `File operation failed.` over echoing a user path.

## Exception logging

`LogError(exception, "...")` is acceptable for exceptions raised by first-party code, because
their messages are written by this repository. The exception object is logged so the stack trace
is retained.

If a component may raise an exception whose message embeds remote response data or secrets, do
**not** pass the exception object. Log a sanitized message instead:

```csharp
_logger.LogError(
    "Remote provider request failed. {Detail}",
    SensitiveDataSanitizer.Sanitize(exception.Message));
```

`SensitiveDataSanitizer` is a small defensive helper, not a DLP engine. It redacts bearer
tokens, `key=value` secret assignments, vendor-prefixed keys, and JWTs. See
`docs/error-handling.md` for how exceptions become user-facing errors.

## Cancellation

`OperationCanceledException` is expected control flow, not a failure. Log it at `Information`
(or `Debug`) and rethrow so the caller still sees it. Never log cancellation at `Error`.

```csharp
catch (OperationCanceledException)
{
    _logger.LogInformation("AI message request cancelled by caller.");
    throw;
}
```

## Unhandled exceptions

`App` subscribes to `Application.UnhandledException` and logs `Critical` with the mapped error
code, then deliberately leaves `Handled = false`. Without a user-facing error surface, an
unhandled exception may leave the app inconsistent, so the runtime is allowed to terminate. A
later UI step will add selective handling and a safe error display.

`TaskScheduler.UnobservedTaskException` is treated as a diagnostic only: it logs
`Unobserved task exception detected.` and calls `SetObserved()`. It is not a substitute for
proper `try`/`catch` and `CancellationToken` usage in async paths.

## Performance

Log meaningful events, not every property or message. Do not serialize large objects, log full
collections, or log large file lists. Clipboard and conversation content are never logged.

## Future providers

The pipeline is centralized so file logging, Windows Event Log, OpenTelemetry, Application
Insights, or enterprise monitoring can be added in `LoggingExtensions` without touching call
sites. Provider-specific DLP belongs in the provider, not in application code.
