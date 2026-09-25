# Secret handling

The configuration foundation does not implement or store application secrets.

## Prohibited locations

API keys, access tokens, passwords, certificates, and other credentials must not be stored in:

- `appsettings.json`
- `appsettings.Development.json`
- Source code
- Tests
- Documentation
- Git history
- Logs

Non-secret application configuration remains version controlled so builds are reproducible.

## Future strategy

Secrets will be resolved separately from strongly typed non-secret options:

1. Use Windows Credential Manager for per-user desktop credentials.
2. Use environment variables for local development when appropriate.
3. Use approved enterprise secret providers for managed deployments when required.
4. Support secret rotation and deletion without changing application configuration.

No Windows Credential Manager implementation or secret-storage abstraction is introduced in this configuration-only step. Future infrastructure code should expose secret values only to the provider that needs them and must not copy them into general options objects.

## Safety rules

- Never log a complete options object or configuration section.
- Never log secret identifiers together with their values.
- Never include credentials in exception messages.
- Never persist raw secret values in normal settings or conversation storage.
- Keep `.env`, `.env.*`, `secrets.json`, `*.secrets.json`, and `local.settings.json` untracked.

Environment variables are a development transport, not long-term secure storage. Production credentials should use the operating-system or enterprise secret store selected by the later security implementation.
