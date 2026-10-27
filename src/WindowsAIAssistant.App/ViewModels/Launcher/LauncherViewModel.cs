using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.App.Services.Launcher;
using WindowsAIAssistant.Application.Launcher;
using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.App.ViewModels.Launcher;

/// <summary>
/// The launcher window's state: the query, the grouped rows, and the one row the keyboard is on.
/// <para>
/// It deliberately owns no search or launch logic. A keystroke is debounced, the operation is
/// cancelled when the text changes again, and the Application layer decides what the text means.
/// </para>
/// </summary>
public sealed partial class LauncherViewModel : ObservableObject, IDisposable
{
    private readonly ILauncherSearchService _search;
    private readonly LauncherActionService _actions;
    private readonly LauncherSearchPolicy _policy;
    private readonly ILogger<LauncherViewModel> _logger;
    private readonly CancellationTokenSource _lifetime = new();

    private CancellationTokenSource? _searchCancellation;
    private int _searchVersion;
    private bool _disposed;

    public LauncherViewModel(
        ILauncherSearchService search,
        LauncherActionService actions,
        LauncherSearchPolicy policy,
        ILogger<LauncherViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(logger);

        _search = search;
        _actions = actions;
        _policy = policy;
        _logger = logger;
    }

    /// <summary>Gets the text in the search box.</summary>
    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    /// <summary>Gets the row the keyboard is on.</summary>
    [ObservableProperty]
    public partial LauncherItemViewModel? SelectedItem { get; set; }

    /// <summary>Gets a line explaining the current outcome.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether a search is running.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Gets the rows, grouped under their headings.</summary>
    public ObservableCollection<LauncherGroupViewModel> Groups { get; } = [];

    public bool HasResults => Groups.Count > 0;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    /// <summary>Resets the window for a fresh activation and focuses the search box.</summary>
    public void PrepareForShow()
    {
        StatusText = string.Empty;
        SelectedItem = null;

        if (Query.Length == 0)
        {
            StartSearch();
        }
        else
        {
            Query = string.Empty;
        }
    }

    /// <summary>Runs the selected row, or explains why there is nothing to run.</summary>
    public async Task ExecuteSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedItem is not { } selected)
        {
            StatusText = "Type a command, an application, a file, a question, or an expression.";
            return;
        }

        try
        {
            var result = await _actions
                .ExecuteAsync(selected.Item, cancellationToken)
                .ConfigureAwait(true);

            if (result.IsFailure)
            {
                StatusText = result.ErrorMessage ?? "That action could not be completed.";
            }
            else if (!string.IsNullOrWhiteSpace(result.SuccessMessage))
            {
                StatusText = result.SuccessMessage;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Activating a launcher row failed.");
            StatusText = "That action could not be completed. The details are in the log.";
        }
    }

    /// <summary>Moves the selection by one row, wrapping at either end.</summary>
    public void MoveSelection(int delta)
    {
        var items = Groups.SelectMany(group => group.Items).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var current = SelectedItem is null
            ? -1
            : Array.FindIndex(items, item => item.Id == SelectedItem.Id);

        var next = current < 0
            ? (delta > 0 ? 0 : items.Length - 1)
            : (current + delta + items.Length) % items.Length;

        SelectedItem = items[next];
    }

    partial void OnQueryChanged(string value)
    {
        _ = value;
        StartSearch();
    }

    private void StartSearch()
    {
        if (_disposed)
        {
            return;
        }

        var version = ++_searchVersion;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var previous = Interlocked.Exchange(ref _searchCancellation, cancellation);
        previous?.Cancel();

        _ = SearchAsync(version, cancellation);
    }

    private async Task SearchAsync(int version, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;

        try
        {
            if (Query.Length > 0 && _policy.DebounceMilliseconds > 0)
            {
                await Task.Delay(_policy.DebounceMilliseconds, token).ConfigureAwait(true);
            }

            token.ThrowIfCancellationRequested();
            IsBusy = true;

            var result = await _search
                .SearchAsync(
                    new LauncherRequest
                    {
                        Text = Query,
                        MaximumResults = _policy.MaximumResults,
                    },
                    token)
                .ConfigureAwait(true);

            if (version != _searchVersion || token.IsCancellationRequested)
            {
                return;
            }

            Apply(result);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke owns the window now.
        }
        catch (Exception exception)
        {
            if (version == _searchVersion)
            {
                _logger.LogError(exception, "Launcher search failed in the view model.");
                StatusText = "Search could not finish. Try again.";
            }
        }
        finally
        {
            if (version == _searchVersion)
            {
                IsBusy = false;
            }

            Interlocked.CompareExchange(ref _searchCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    private void Apply(LauncherResult result)
    {
        Groups.Clear();

        if (result.IsSuccess)
        {
            foreach (var group in result.Items
                .GroupBy(item => string.IsNullOrWhiteSpace(item.Group) ? "Results" : item.Group)
                .Select(group => new LauncherGroupViewModel(
                    group.Key,
                    group.Select(item => new LauncherItemViewModel(item)).ToArray())))
            {
                Groups.Add(group);
            }
        }

        OnPropertyChanged(nameof(HasResults));
        SelectedItem = Groups.SelectMany(group => group.Items).FirstOrDefault();
        StatusText = result.Message
            ?? (result.IsSuccess
                ? (HasResults ? string.Empty : "Nothing matched. Keep typing.")
                : "Search could not finish.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        Interlocked.Exchange(ref _searchCancellation, null)?.Cancel();
        _lifetime.Dispose();
    }
}
