using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.App.ViewModels.Launcher;

/// <summary>One presentable launcher row.</summary>
public sealed record LauncherItemViewModel(LauncherItem Item)
{
    public string Id => Item.Id;

    public string Title => Item.Title;

    public string Description => Item.Description;

    public string Glyph => Item.Glyph;

    public string Group => Item.Group;

    public string AccessibleName => string.IsNullOrWhiteSpace(Item.Description)
        ? Item.Title
        : $"{Item.Title}. {Item.Description}";
}
