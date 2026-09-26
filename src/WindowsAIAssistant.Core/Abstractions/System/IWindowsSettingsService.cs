using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Core.Abstractions.System;

/// <summary>
/// Maps a spoken Windows Settings page name to a supported settings address.
/// <para>
/// The mapping table lives in the platform implementation, so the URI scheme is never
/// duplicated into shared code or into a voice command pattern.
/// </para>
/// </summary>
public interface IWindowsSettingsService
{
    /// <summary>Gets the settings page names this machine supports.</summary>
    IReadOnlyCollection<string> SupportedPages { get; }

    /// <summary>Resolves a page name such as "bluetooth" or "windows update" to a URI.</summary>
    Result<Uri> ResolvePageUri(string pageName);
}
