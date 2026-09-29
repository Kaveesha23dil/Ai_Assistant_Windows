using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using IScreenCaptureHost = WindowsAIAssistant.Core.Abstractions.Vision.IScreenCaptureHost;

namespace WindowsAIAssistant.App.Services;

/// <summary>
/// Connects the screen capture feature to the application's one window and one UI thread.
/// <para>
/// The Windows capture picker is a user interface object. It has to be constructed on a thread
/// with a message loop, shown as a child of a real window, and awaited until a person has
/// chosen. Infrastructure cannot know any of that — it has no window and no dispatcher — so this
/// type is the whole of what the application layer owes the feature, and it is deliberately
/// small: a handle, a name, and a way to get onto the right thread.
/// </para>
/// <para>
/// The work is named by the caller and started on the host thread rather than run on it, so
/// nobody is tempted to await a picker on the thread that has to keep painting. The caller's own
/// cancellation unwinds to the caller as a cancellation, and a picker that is already open cannot
/// be dismissed from here — Windows gives no way to ask — so an abandoned request stops waiting
/// without pretending to have closed the window.
/// </para>
/// </summary>
public sealed class WindowCaptureHost : IScreenCaptureHost, IDisposable
{
    private readonly ILogger<WindowCaptureHost> _logger;
    private readonly Lock _gate = new();

    private IntPtr _handle;
    private DispatcherQueue? _dispatcher;
    private bool _disposed;

    public WindowCaptureHost(ILogger<WindowCaptureHost> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public IntPtr WindowHandle
    {
        get
        {
            lock (_gate)
            {
                return _handle;
            }
        }
    }

    /// <inheritdoc />
    public string DisplayName => "Windows AI Assistant";

    /// <inheritdoc />
    public event EventHandler? HostChanged;

    /// <summary>
    /// Publishes the window the picker belongs to. Called by the shell once it exists.
    /// <para>
    /// Publishes a dispatcher rather than a thread, because that is the only handle that stays
    /// valid: a thread can be identified from a window, but a window can be recreated, and a
    /// captured dispatcher keeps working across that.
    /// </para>
    /// </summary>
    public void Attach(IntPtr handle, DispatcherQueue dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        lock (_gate)
        {
            _handle = handle;
            _dispatcher = dispatcher;
        }

        _logger.LogInformation("The screen capture host window is available.");
        HostChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Forgets the window, so a request that arrives after the shell has closed fails with an
    /// explanation rather than opening a picker nobody can see.
    /// </summary>
    public void Detach()
    {
        lock (_gate)
        {
            _handle = IntPtr.Zero;
            _dispatcher = null;
        }

        HostChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task<T> InvokeOnHostThreadAsync<T>(
        Func<Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        DispatcherQueue? dispatcher;
        lock (_gate)
        {
            dispatcher = _dispatcher;
        }

        if (dispatcher is null)
        {
            throw new InvalidOperationException(
                "The application window is not open yet, so the capture picker has nowhere to appear.");
        }

        if (dispatcher.HasThreadAccess)
        {
            return await work().ConfigureAwait(false);
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The completion source owns the work's result rather than the caller's stack frame, so a
        // caller that is waiting on a dispatcher turn is not the thing holding the delegate alive
        // while the picker sits on screen for two minutes.
        await using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<T>)state!).TrySetCanceled(),
            completion);

        // Cast because the queue has an overload taking the handler alone, and a bare lambda
        // binds to that one first and then fails on the priority. Naming the delegate type
        // settles it, and the cast costs nothing at runtime.
        DispatcherQueueHandler start = () => _ = StartOnHostThread(work, completion);

        if (!dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, start))
        {
            throw new InvalidOperationException(
                "The application's window could not accept the capture request.");
        }

        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the caller's work on the host thread and completes the caller's task with it.
    /// <para>
    /// Awaited nowhere and returned nowhere on purpose. It has already handed its result to the
    /// caller's completion source, and awaiting it here would mean blocking the thread that has
    /// to keep the window responsive until a person finishes choosing.
    /// </para>
    /// </summary>
    private async Task StartOnHostThread<T>(Func<Task<T>> work, TaskCompletionSource<T> completion)
    {
        try
        {
            completion.TrySetResult(await work().ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled();
        }
        catch (Exception exception)
        {
            // Recorded here so a refusal to show the picker is visible in the log, but the
            // exception itself travels to the caller, which knows how to put it in front of
            // somebody in words that are about their screen.
            _logger.LogWarning(
                "Work on the host thread failed with {ErrorType}.",
                exception.GetType().Name);

            completion.TrySetException(exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Detach();
    }
}
