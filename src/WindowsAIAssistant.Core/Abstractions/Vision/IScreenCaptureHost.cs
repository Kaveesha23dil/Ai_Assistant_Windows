namespace WindowsAIAssistant.Core.Abstractions.Vision;

/// <summary>
/// Supplies the window that the Windows capture picker belongs to.
/// <para>
/// The picker is a child of a real window, and a picker with no parent behaves badly: it can
/// open behind the application, steal focus, and stay on screen after the request is gone. The
/// handle cannot be discovered from Infrastructure, which has no idea a window exists, so the
/// application layer publishes it once through this seam and Infrastructure asks.
/// </para>
/// <para>
/// An interface rather than a static so that a headless test can supply a handle or report that
/// there is none, instead of the composition root reaching into a window from four layers down.
/// </para>
/// </summary>
public interface IScreenCaptureHost
{
    /// <summary>
    /// Gets the handle of the window the picker should belong to, or
    /// <see cref="IntPtr.Zero"/> when no window is up yet.
    /// </summary>
    IntPtr WindowHandle { get; }

    /// <summary>Gets a name for the host, used in picker UI where a title is required.</summary>
    string DisplayName { get; }

    /// <summary>Raises when the host window appears or is replaced.</summary>
    event EventHandler? HostChanged;

    /// <summary>
    /// Runs work on the thread that owns the host window and returns what it produced.
    /// <para>
    /// The Windows capture picker is a user interface object: it can only be constructed and
    /// shown from a thread that has a window message loop, and on this platform that is the
    /// application's user interface thread. A capture request can arrive from anywhere — a voice
    /// command handled on a background thread, a timer, a test — so the caller names the work and
    /// this method moves it to the thread that is allowed to do it, then hands the result back on
    /// the caller's thread.
    /// </para>
    /// <para>
    /// The delegate returns a task rather than a value because the picker is awaited: the person
    /// is choosing a window, and that takes as long as it takes. The work is started on the host
    /// thread and its completion is observed by the caller, so a slow choice does not block the
    /// interface and a cancelled one unwinds to the caller as
    /// <see cref="TaskCanceledException"/>.
    /// </para>
    /// </summary>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="work">The work to run on the host's thread.</param>
    /// <param name="cancellationToken">Signals that the person no longer wants the result.</param>
    /// <returns>A task that completes with the work's result.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when there is no host thread to run on, which means no window is up and the picker
    /// has nowhere to appear.
    /// </exception>
    Task<T> InvokeOnHostThreadAsync<T>(
        Func<Task<T>> work,
        CancellationToken cancellationToken = default);
}
