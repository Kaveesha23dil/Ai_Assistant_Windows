using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// Identifies the top-level sections of the application shell.
/// </summary>
public enum AppSection
{
    Home,
    Chat,
    Files,
    Automations,
    Settings
}

/// <summary>
/// Shell-level state for the main window. Navigation itself is still handled directly by
/// the view until the dedicated navigation service is introduced.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string ApplicationTitle { get; set; } = "Windows AI Assistant";

    [ObservableProperty]
    public partial AppSection SelectedSection { get; set; } = AppSection.Home;

    [ObservableProperty]
    public partial bool IsNavigationPaneOpen { get; set; } = true;

    public MainViewModel(IOptions<ApplicationOptions> applicationOptions, IOptions<UIOptions> uiOptions)
    {
        ArgumentNullException.ThrowIfNull(applicationOptions);
        ArgumentNullException.ThrowIfNull(uiOptions);

        if (!string.IsNullOrWhiteSpace(applicationOptions.Value.Name))
        {
            ApplicationTitle = applicationOptions.Value.Name;
        }

        if (Enum.TryParse(uiOptions.Value.DefaultPage, ignoreCase: true, out AppSection defaultSection))
        {
            SelectedSection = defaultSection;
        }

        CurrentTheme = uiOptions.Value.Theme;
    }

    /// <summary>
    /// Gets the configured theme name, applied once when the window is created.
    /// </summary>
    public string CurrentTheme { get; }

    public string SectionTitle => SelectedSection switch
    {
        AppSection.Home => "Home",
        AppSection.Chat => "Chat",
        AppSection.Files => "Files",
        AppSection.Automations => "Automations",
        AppSection.Settings => "Settings",
        _ => ApplicationTitle
    };

    partial void OnSelectedSectionChanged(AppSection value) => OnPropertyChanged(nameof(SectionTitle));
}
