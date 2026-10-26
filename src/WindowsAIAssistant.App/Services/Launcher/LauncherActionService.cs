using Microsoft.Extensions.Logging;
using WindowsAIAssistant.App.Services.Navigation;
using WindowsAIAssistant.App.Views;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Abstractions.Navigation;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Launcher;
using WinRT.Interop;

namespace WindowsAIAssistant.App.Services.Launcher;

/// <summary>
/// Performs what a chosen launcher row says. Each action goes through the abstraction that
/// already owns it, so the launcher is a new entrance to the same safe paths rather than a
/// second implementation of them.
/// </summary>
public sealed class LauncherActionService
{
    private static readonly HashSet<string> AllowedUriSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https", "ms-settings" };

    private readonly IApplicationLauncherService _applications;
    private readonly IUriLauncherService _uris;
    private readonly IClipboardService _clipboard;
    private readonly INavigationService _navigation;
    private readonly IAssistantLauncherService _launcher;
    private readonly LauncherHandoffService _handoff;
    private readonly MainWindow _mainWindow;
    private readonly ILogger<LauncherActionService> _logger;

    public LauncherActionService(
        IApplicationLauncherService applications,
        IUriLauncherService uris,
        IClipboardService clipboard,
        INavigationService navigation,
        IAssistantLauncherService launcher,
        LauncherHandoffService handoff,
        MainWindow mainWindow,
        ILogger<LauncherActionService> logger)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(uris);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(logger);

        _applications = applications;
        _uris = uris;
        _clipboard = clipboard;
        _navigation = navigation;
        _launcher = launcher;
        _handoff = handoff;
        _mainWindow = mainWindow;
        _logger = logger;
    }

    /// <summary>Runs one row's action. A failure keeps the launcher open so it can be explained.</summary>
    public async Task<Result> ExecuteAsync(
        LauncherItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        try
        {
            return item.Action.Kind switch
            {
                LauncherActionKind.LaunchApplication => await LaunchAsync(item.Action.Target, cancellationToken)
                    .ConfigureAwait(true),
                LauncherActionKind.OpenFile => await OpenFileAsync(item.Action.Target, cancellationToken)
                    .ConfigureAwait(true),
                LauncherActionKind.OpenUri => await OpenUriAsync(item.Action.Target, cancellationToken)
                    .ConfigureAwait(true),
                LauncherActionKind.CopyText => await CopyAsync(item.Action.Target, cancellationToken)
                    .ConfigureAwait(true),
                LauncherActionKind.Navigate => await NavigateAsync(item.Action.Target, null)
                    .ConfigureAwait(true),
                LauncherActionKind.OpenKnowledge => await NavigateAsync(
                        nameof(NavigationRoute.Knowledge),
                        item.Action.Target)
                    .ConfigureAwait(true),
                LauncherActionKind.AskAssistant => await AskAssistantAsync(item.Action.Target)
                    .ConfigureAwait(true),
                _ => Result.Failure(ErrorCodes.LauncherActionFailed, "That result has no action."),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A launcher action failed.");
            return Result.Failure(
                ErrorCodes.LauncherActionFailed,
                "That action could not be completed. The details are in the log.");
        }
    }

    private async Task<Result> LaunchAsync(string? target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return MissingTarget();
        }

        var result = await _applications.LaunchAsync(target, cancellationToken).ConfigureAwait(true);
        return await CompleteAsync(result).ConfigureAwait(true);
    }

    private async Task<Result> OpenFileAsync(string? target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target) || !Path.IsPathFullyQualified(target))
        {
            return MissingTarget();
        }

        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return Result.Failure(ErrorCodes.LauncherActionFailed, "That file path could not be opened.");
        }

        var result = await _uris.OpenAsync(uri, cancellationToken).ConfigureAwait(true);
        return await CompleteAsync(result).ConfigureAwait(true);
    }

    private async Task<Result> OpenUriAsync(string? target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target)
            || !Uri.TryCreate(target, UriKind.Absolute, out var uri)
            || !AllowedUriSchemes.Contains(uri.Scheme))
        {
            return Result.Failure(ErrorCodes.LauncherActionFailed, "That address cannot be opened.");
        }

        var result = await _uris.OpenAsync(uri, cancellationToken).ConfigureAwait(true);
        return await CompleteAsync(result).ConfigureAwait(true);
    }

    private async Task<Result> CopyAsync(string? target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return MissingTarget();
        }

        var result = await _clipboard.SetTextAsync(target, cancellationToken).ConfigureAwait(true);
        if (result.IsFailure)
        {
            return result;
        }

        return Result.Success($"Copied {target}");
    }

    private async Task<Result> NavigateAsync(string? target, object? parameter)
    {
        if (!Enum.TryParse<NavigationRoute>(target, ignoreCase: true, out var route)
            || !_navigation.CanNavigateTo(route))
        {
            return Result.Failure(ErrorCodes.LauncherActionFailed, "That destination is not available.");
        }

        if (!_navigation.Navigate(route, parameter))
        {
            return Result.Failure(ErrorCodes.LauncherActionFailed, "That destination could not be opened.");
        }

        ActivateMainWindow();
        var result = Result.Success();
        return await CompleteAsync(result).ConfigureAwait(true);
    }

    private async Task<Result> AskAssistantAsync(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return MissingTarget();
        }

        _handoff.PostQuestion(target);

        if (!_navigation.Navigate(NavigationRoute.Chat))
        {
            return Result.Failure(ErrorCodes.LauncherActionFailed, "Chat could not be opened.");
        }

        ActivateMainWindow();
        return await CompleteAsync(Result.Success()).ConfigureAwait(true);
    }

    private async Task<Result> CompleteAsync(Result result)
    {
        if (result.IsFailure)
        {
            return result;
        }

        await _launcher.HideAsync().ConfigureAwait(true);
        return result;
    }

    private void ActivateMainWindow()
    {
        try
        {
            ForegroundWindowService.Activate(WindowNative.GetWindowHandle(_mainWindow));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The main window could not be brought to the foreground.");
        }
    }

    private static Result MissingTarget() =>
        Result.Failure(ErrorCodes.LauncherActionFailed, "That result is missing what it needs to run.");
}
