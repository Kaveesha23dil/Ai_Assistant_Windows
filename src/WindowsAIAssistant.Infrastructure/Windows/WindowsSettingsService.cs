using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Infrastructure.Windows;

/// <summary>
/// Maps a spoken Windows Settings page name to a supported settings address.
/// <para>
/// The mapping table lives here, in the platform project, so the URI scheme never appears in
/// shared code or in a voice command pattern. The handler passes a matched name from the
/// intent table and this class decides whether it is real; an unrecognized name produces a
/// failure rather than a URI built from the transcript.
/// </para>
/// </summary>
public sealed class WindowsSettingsService : IWindowsSettingsService
{
    private const string Scheme = "ms-settings:";

    private static readonly IReadOnlyDictionary<string, string> PageUris =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["settings"] = $"{Scheme}home",
            ["home"] = $"{Scheme}home",
            ["system"] = $"{Scheme}system",
            ["display"] = $"{Scheme}display",
            ["sound"] = $"{Scheme}sound",
            ["audio"] = $"{Scheme}sound",
            ["volume"] = $"{Scheme}sound",
            ["notifications"] = $"{Scheme}notifications",
            ["power"] = $"{Scheme}power",
            ["battery"] = $"{Scheme}batterysaver",
            ["storage"] = $"{Scheme}storagesense",
            ["privacy"] = $"{Scheme}privacy",
            ["network"] = $"{Scheme}network",
            ["network and internet"] = $"{Scheme}network",
            ["internet"] = $"{Scheme}network",
            ["wifi"] = $"{Scheme}wifi",
            ["wireless"] = $"{Scheme}wifi",
            ["bluetooth"] = $"{Scheme}bluetooth",
            ["personalization"] = $"{Scheme}personalization",
            ["background"] = $"{Scheme}personalization-background",
            ["homepage"] = $"{Scheme}personalization-start",
            ["apps"] = $"{Scheme}appsfeatures",
            ["default apps"] = $"{Scheme}defaultapps",
            ["account"] = $"{Scheme}yourinfo",
            ["about"] = $"{Scheme}about",
            ["update"] = $"{Scheme}windowsupdate",
            ["windows update"] = $"{Scheme}windowsupdate",
            ["printers"] = $"{Scheme}printers",
            ["device manager"] = $"{Scheme}devicemanager",
            ["security"] = $"{Scheme}security",
            ["mouse"] = $"{Scheme}mouse",
            ["keyboard"] = $"{Scheme}keyboard",
            ["recovery"] = $"{Scheme}recovery"
        };

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedPages => PageUris.Keys.ToArray();

    /// <inheritdoc />
    public Result<Uri> ResolvePageUri(string pageName)
    {
        if (string.IsNullOrWhiteSpace(pageName))
        {
            return Result<Uri>.Failure("No settings page was requested.");
        }

        var key = pageName.Trim();

        // "bluetooth settings" and "display settings" reach here from the intent table, and a
        // user may say the same thing, so the trailing noun is trimmed before the lookup.
        if (key.EndsWith(" settings", StringComparison.OrdinalIgnoreCase))
        {
            key = key[..^" settings".Length].Trim();
        }

        if (!PageUris.TryGetValue(key, out var address)
            || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return Result<Uri>.Failure($"There is no settings page called {key}.");
        }

        return Result<Uri>.Success(uri);
    }
}
