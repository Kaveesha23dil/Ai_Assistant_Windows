using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Core.Abstractions.System;

/// <summary>
/// Hands a URI to the platform shell so it opens in the user's default handler.
/// <para>
/// This is the only route by which the assistant opens a web page, a Windows Settings page,
/// or a folder. Callers pass a <see cref="Uri"/> they built themselves; the service never
/// assembles a command line from text.
/// </para>
/// </summary>
public interface IUriLauncherService
{
    /// <summary>Opens the URI using the platform shell.</summary>
    Task<Result> OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}
