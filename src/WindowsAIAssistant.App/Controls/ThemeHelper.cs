using Microsoft.UI.Xaml;

namespace WindowsAIAssistant.App.Controls;

/// <summary>
/// Translates the theme names used in configuration and settings into WinUI element themes.
/// </summary>
public static class ThemeHelper
{
    public static ElementTheme Resolve(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default
    };
}
