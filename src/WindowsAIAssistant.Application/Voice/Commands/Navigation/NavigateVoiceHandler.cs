using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Navigation;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.Navigation;

/// <summary>
/// Carries out a spoken request to move between this application's own pages.
/// <para>
/// The handler translates one thing: a spoken destination becomes a route, and the route is
/// handed to <see cref="IApplicationNavigator"/>. It never touches a page, a frame, or a
/// window, and it performs no work of its own. That is what makes a spoken "open settings"
/// produce exactly the same result as clicking Settings in the sidebar: the sidebar does not
/// have its own route either, it asks the same service.
/// </para>
/// <para>
/// There is no permission check. Moving between the assistant's own pages neither reads
/// anything from the machine nor changes a system setting, so requiring consent would only
/// train someone to say yes to a harmless request.
/// </para>
/// <para>
/// An unrecognised destination is refused rather than guessed at. Sending somebody to an
/// unrelated page because a phrase was misheard is a worse outcome than saying so.
/// </para>
/// </summary>
public sealed class NavigateVoiceHandler : VoiceHandlerBase
{
    private static readonly AssistantIntent[] Supported = [AssistantIntent.Navigate];

    private readonly IApplicationNavigator _navigator;

    public NavigateVoiceHandler(
        IApplicationNavigator navigator,
        IPermissionService permissions,
        ILogger<NavigateVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(navigator);

        _navigator = navigator;
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

        var spoken = command.GetParameter(VoiceCommand.NavigationDestinationParameter);
        if (spoken is null)
        {
            return Task.FromResult(Invalid(command, "I didn't catch which page you meant."));
        }

        if (!NavigationRouteExtensions.TryParse(spoken, out var route))
        {
            return Task.FromResult(Invalid(command, $"I don't have a page called {spoken}."));
        }

        // Already on the page is a legitimate answer, not a failure: repeating the request is
        // a natural thing to do and should not produce an apologetic error.
        if (_navigator.CurrentRoute == route)
        {
            return Task.FromResult(VoiceCommandResult.Success(
                command,
                $"You're already on {route.ToDisplayName()}."));
        }

        if (!_navigator.CanNavigateTo(route))
        {
            return Task.FromResult(Unavailable(
                command,
                $"The {route.ToDisplayName()} page isn't available yet."));
        }

        try
        {
            return Task.FromResult(_navigator.Navigate(route)
                ? VoiceCommandResult.Success(command, $"Opening {route.ToDisplayName()}.")
                : Unavailable(command, $"I couldn't open {route.ToDisplayName()}."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Task.FromResult(Failed(command, exception));
        }
    }
}
