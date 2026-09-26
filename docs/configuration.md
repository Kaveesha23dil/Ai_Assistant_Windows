# Configuration

Windows AI Assistant uses the standard .NET host configuration pipeline and the Options pattern. Runtime services receive validated, strongly typed options instead of reading arbitrary configuration keys.

## Sources and precedence

Configuration is loaded in this order for this step:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. Environment variables
4. A future secure secret provider, when implemented

Later sources override earlier sources. `Host.CreateDefaultBuilder()` supplies the standard JSON and environment-variable behavior; the application does not add duplicate JSON sources.

The configuration files are copied to the application output with `CopyToOutputDirectory="PreserveNewest"`.

## Environment selection

Set the standard host environment variable before starting the application:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project src/WindowsAIAssistant.App -p:Platform=x64
```

`Development` loads `appsettings.Development.json`. `Production` uses only the base file. A Debug build does not automatically select Development, so `DOTNET_ENVIRONMENT` must be set when development overrides are required.

## Configuration files

- `src/WindowsAIAssistant.App/appsettings.json` contains reviewed, non-secret defaults.
- `src/WindowsAIAssistant.App/appsettings.Development.json` contains only development overrides and logging levels.

Neither file contains credentials.

## Options

| Class | Section | Purpose |
|---|---|---|
| `ApplicationOptions` | `Application` | Application identity, environment label, and diagnostics flag |
| `AIOptions` | `AI` | Mock-ready AI provider settings and generic limits |
| `FeatureOptions` | `Features` | Configuration-level feature flags |
| `PrivacyOptions` | `Privacy` | Privacy-sensitive capability permissions |
| `UIOptions` | `UI` | Theme and launch preferences for future UI behavior |
| `VoiceOptions` | `Voice` | Speech, confidence, confirmation, and response-length behaviour |

All options are registered by `AddApplicationConfiguration` and validated on host startup. Static startup settings should be consumed with `IOptions<T>`. `IOptionsMonitor<T>` and `IOptionsSnapshot<T>` should be introduced only if their scoped or changing behavior is required.

`VoiceOptions` is read by the App composition root, which maps it onto `VoiceIntentPolicy` so that the Application layer stays free of configuration types. The settings screen currently shows these values and edits them in memory only; changes made there do not yet reach a running session.

## Environment-variable overrides

Use the section name, double underscores, and property name. For example:

```text
AI__Provider=Local
AI__Model=local-model
Features__EnableVoice=false
Privacy__AllowCloudAI=false
UI__Theme=Dark
```

In PowerShell, the same override can be set for the current process as follows:

```powershell
$env:AI__Provider = "Local"
```

Environment variables must not contain machine-specific settings committed to the repository.

## Validation

Application options require a non-empty name and an environment of `Development` or `Production`.

AI options require:

- A non-empty provider.
- A non-empty model.
- A temperature from `0.0` through `2.0`.
- An output-token limit greater than zero.
- A request timeout from `1` through `600` seconds.

An empty AI provider fails validation even when AI chat is enabled. Local AI does not require cloud permission. The validation model intentionally remains provider-neutral.

## Feature flags

The committed defaults enable only mock-ready capabilities:

- AI chat
- File search
- Clipboard
- System information

Automation, screen AI, and local AI are disabled. Voice is implemented but disabled by default: the `Voice` section governs recognition, and the privacy switches below decide which capabilities it may reach.

## Privacy defaults

All sensitive capabilities default to disabled:

| Setting | Default |
|---|---|
| `AllowCloudAI` | `false` |
| `AllowTelemetry` | `false` |
| `AllowClipboardProcessing` | `false` |
| `AllowFileIndexing` | `false` |
| `AllowScreenAnalysis` | `false` |
| `StoreConversationHistory` | `false` |
| `AllowMicrophoneAccess` | `false` |
| `AllowVoiceProcessing` | `false` |
| `AllowCloudSpeechProcessing` | `false` |
| `AllowScreenCapture` | `false` |
| `AllowSystemControl` | `false` |
| `StoreVoiceHistory` | `false` |
| `AllowApplicationLaunch` | `true` |
| `AllowWebSearch` | `true` |

Each spoken capability maps onto one of these: voice input needs `AllowMicrophoneAccess` *and* `AllowVoiceProcessing`, file search follows `AllowFileIndexing`, and screenshots follow `AllowScreenCapture`.

A voice command is refused unless the matching capability is granted, so enabling `Voice` alone does not grant anything. Application launching and web search are on by default because they only ever act on an allow-listed name or a query the user spoke aloud, and both are still gated behind spoken confirmation.

The settings screen exposes these switches, but edits are in memory for now and are not written back to configuration.

## Secrets

Credentials are not configuration values and must not be placed in JSON, source code, logs, or Git. See [secrets.md](secrets.md) for the deferred secure-storage strategy.
