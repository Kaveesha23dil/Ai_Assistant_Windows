using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Abstractions.Web;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.WebSearch;

/// <summary>
/// Handles "search the web for X" and the site-specific variants such as YouTube or GitHub.
/// <para>
/// One handler serves both intents because the only difference between them is which search
/// provider the intent rule selected. The query is URL-encoded by the provider and handed to
/// the shell as a URI; no string is ever concatenated into a command line.
/// </para>
/// </summary>
public sealed class WebSearchVoiceHandler : VoiceHandlerBase
{
    private static readonly AssistantIntent[] Supported =
        [AssistantIntent.WebSearch, AssistantIntent.YouTubeSearch];

    private readonly IReadOnlyList<IWebSearchProvider> _providers;
    private readonly IUriLauncherService _uriLauncher;

    public WebSearchVoiceHandler(
        IEnumerable<IWebSearchProvider> providers,
        IUriLauncherService uriLauncher,
        IPermissionService permissions,
        ILogger<WebSearchVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(uriLauncher);

        _providers = providers.ToArray();
        _uriLauncher = uriLauncher;
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

        if (!Permissions.IsGranted(PermissionCapability.WebSearch))
        {
            return Denied(command, PermissionCapability.WebSearch);
        }

        if (!TryGetRequiredParameter(
                command,
                VoiceCommand.QueryParameter,
                out var query,
                out var failure))
        {
            return failure;
        }

        var provider = ResolveProvider(command);
        if (provider is null)
        {
            return Unavailable(command, "That search site isn't available.");
        }

        var uri = provider.BuildSearchUri(query);
        if (uri.IsFailure || uri.Value is null)
        {
            return Invalid(command, $"I couldn't build a {provider.Name} search for that.");
        }

        try
        {
            var opened = await _uriLauncher.OpenAsync(uri.Value, cancellationToken).ConfigureAwait(false);
            return opened.IsSuccess
                ? VoiceCommandResult.Success(command, $"Searching {provider.Name} for {query}.")
                : Unavailable(command, $"I couldn't open {provider.Name}.");
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

    /// <summary>
    /// Picks the provider the intent rule named, falling back to the default engine for a
    /// plain "search the web" request.
    /// </summary>
    private IWebSearchProvider? ResolveProvider(VoiceCommand command)
    {
        var requested = command.GetParameter(VoiceCommand.SearchProviderParameter);
        if (string.IsNullOrWhiteSpace(requested)
            && command.Intent == AssistantIntent.YouTubeSearch)
        {
            // The intent already names the site, so a command that lost its parameter still
            // searches where the user asked instead of quietly falling back to the web.
            requested = WebSearchProviderType.YouTube.ToString();
        }

        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = _providers.FirstOrDefault(provider =>
                string.Equals(provider.ProviderType.ToString(), requested, StringComparison.OrdinalIgnoreCase)
                || string.Equals(provider.Name, requested, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        return _providers.FirstOrDefault(provider => provider.IsDefault) ?? _providers.FirstOrDefault();
    }
}
