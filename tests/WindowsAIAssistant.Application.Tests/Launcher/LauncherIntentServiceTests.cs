using WindowsAIAssistant.Application.Launcher;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Application.Tests.Launcher;

public class LauncherIntentServiceTests
{
    private readonly LauncherIntentService _intents = new();

    [Theory]
    [InlineData("= 2 + 2", LauncherIntentType.Calculator, "2 + 2")]
    [InlineData("2 + 2", LauncherIntentType.Calculator, "2 + 2")]
    [InlineData("10 × 4", LauncherIntentType.Calculator, "10 × 4")]
    [InlineData("calc 9 / 3", LauncherIntentType.Calculator, "9 / 3")]
    [InlineData("> files", LauncherIntentType.Command, "files")]
    [InlineData("/settings", LauncherIntentType.Command, "settings")]
    [InlineData("? what is RAG", LauncherIntentType.AiQuestion, "what is RAG")]
    [InlineData("web local ai", LauncherIntentType.WebSearch, "local ai")]
    [InlineData("google winui", LauncherIntentType.WebSearch, "winui")]
    [InlineData("find file budget.xlsx", LauncherIntentType.File, "budget.xlsx")]
    [InlineData("knowledge privacy policy", LauncherIntentType.Knowledge, "privacy policy")]
    [InlineData("open settings", LauncherIntentType.Setting, "open settings")]
    [InlineData("open notepad", LauncherIntentType.Application, "notepad")]
    public void Route_Recognizes_ExplicitIntent(
        string text,
        LauncherIntentType expectedType,
        string expectedQuery)
    {
        var intent = _intents.Route(text);

        Assert.Equal(expectedType, intent.Type);
        Assert.Equal(expectedQuery, intent.Query);
    }

    [Theory]
    [InlineData("how do I index a folder?")]
    [InlineData("summarize the selected document")]
    [InlineData("what is the weather")]
    public void Route_Sends_AmbiguousQuestions_ToAssistant(string text)
    {
        var intent = _intents.Route(text);

        Assert.Equal(LauncherIntentType.AiQuestion, intent.Type);
        Assert.True(intent.RequiresAi);
    }

    [Fact]
    public void Route_Keeps_PlainText_Universal()
    {
        var intent = _intents.Route("notepad");

        Assert.Equal(LauncherIntentType.Universal, intent.Type);
        Assert.False(intent.RequiresAi);
    }

    [Fact]
    public void Route_Treats_Empty_Text_As_Universal()
    {
        var intent = _intents.Route("   ");

        Assert.Equal(LauncherIntentType.Universal, intent.Type);
        Assert.Equal(string.Empty, intent.Query);
    }
}
