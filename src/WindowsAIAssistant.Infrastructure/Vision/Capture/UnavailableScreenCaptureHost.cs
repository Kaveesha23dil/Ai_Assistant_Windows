using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Exceptions;
using IScreenCaptureHost = WindowsAIAssistant.Core.Abstractions.Vision.IScreenCaptureHost;

namespace WindowsAIAssistant.Infrastructure.Vision.Capture;

/// <summary>
/// Stands in for the capture host on a process that has no window.
/// <para>
/// Registered as a default so that the container is always complete. A service that cannot be
/// built is a service nobody can fall back to, and a headless host — a test, a command, a
/// background tool — should get a feature that says it cannot capture rather than an exception
/// about a missing registration that tells them nothing about their own machine.
/// </para>
/// <para>
/// A host that registers the real one replaces this registration outright, because the container
/// keeps the last registration for a service. That is why the real host is added after this and
/// not before: the order is the whole mechanism, which is worth a comment here and at the other
/// end of it.
/// </para>
/// </summary>
public sealed class UnavailableScreenCaptureHost : IScreenCaptureHost
{
    /// <inheritdoc />
    public IntPtr WindowHandle => IntPtr.Zero;

    /// <inheritdoc />
    public string DisplayName => "This process";

    /// <inheritdoc />
    /// <remarks>
    /// Never raised. Nothing about this host ever changes, and an event that can never fire is
    /// one more thing a caller has to reason about for no benefit.
    /// </remarks>
    public event EventHandler? HostChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    /// <exception cref="ScreenVisionException">
    /// Always, with a code from <see cref="ErrorCodes"/>. There is no thread to move work to, and
    /// running it here instead would mean showing a picker with no parent — which opens behind
    /// whatever is on screen and stays there after the request is gone.
    /// </exception>
    public Task<T> InvokeOnHostThreadAsync<T>(
        Func<Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        throw new ScreenVisionException(
            "Taking a screenshot needs the application window, and this is running without one. "
            + "Reading text and describing documents still work.",
            ErrorCodes.ScreenCaptureUnsupported);
    }
}
