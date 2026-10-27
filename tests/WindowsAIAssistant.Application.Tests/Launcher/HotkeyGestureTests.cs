using WindowsAIAssistant.Core.Models.Launcher;

namespace WindowsAIAssistant.Application.Tests.Launcher;

public class HotkeyGestureTests
{
    [Fact]
    public void Default_Is_AltSpace()
    {
        Assert.Equal(HotkeyModifiers.Alt, HotkeyGesture.Default.Modifiers);
        Assert.Equal("Space", HotkeyGesture.Default.Key);
        Assert.Equal("Alt+Space", HotkeyGesture.Default.DisplayName);
    }

    [Theory]
    [InlineData("Alt+Space", "Alt+Space")]
    [InlineData("ctrl+shift+p", "Ctrl+Shift+P")]
    [InlineData("Win+Alt+F5", "Alt+Win+F5")]
    public void TryParse_Accepts_SupportedShortcut(string text, string displayName)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var hotkey, out var error));
        Assert.Null(error);
        Assert.NotNull(hotkey);
        Assert.Equal(displayName, hotkey.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Space")]
    [InlineData("Alt")]
    [InlineData("Alt+Alt+Space")]
    [InlineData("Hyper+Space")]
    [InlineData("Alt+F25")]
    public void TryParse_Rejects_InvalidShortcut(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out var hotkey, out var error));
        Assert.Null(hotkey);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ParseOrDefault_FallsBack_ToDefault()
    {
        var hotkey = HotkeyGesture.ParseOrDefault("not a shortcut");

        Assert.Equal(HotkeyGesture.Default, hotkey);
    }
}
