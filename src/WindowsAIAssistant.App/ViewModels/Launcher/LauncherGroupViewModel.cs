namespace WindowsAIAssistant.App.ViewModels.Launcher;

/// <summary>A heading and the rows beneath it.</summary>
public sealed record LauncherGroupViewModel(string Key, IReadOnlyList<LauncherItemViewModel> Items);
