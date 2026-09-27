using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Tests.Voice;

/// <summary>
/// Checks that a spoken request to change page reaches the navigation service and nothing else.
/// <para>
/// The intent, the destination, and the boundary with Windows Settings are the three things
/// worth proving here. Recognition decides which intent a sentence means; the handler turns that
/// intent into a route; the router refuses anything it has no executor for. A voice layer that
/// quietly opened the assistant's own settings page for "open settings" would be a genuine
/// defect, so that phrase is asserted to stay a Windows command.
/// </para>
/// <para>
/// No window is involved. The navigator is a stand-in that records routes, which is what lets
/// the whole path from transcript to destination be checked as ordinary logic.
/// </para>
/// </summary>
public sealed class VoiceNavigationTests
{
    [Theory]
    [InlineData("go home", NavigationRoute.Home)]
    [InlineData("go to home", NavigationRoute.Home)]
    [InlineData("open chat", NavigationRoute.Chat)]
    [InlineData("show files", NavigationRoute.Files)]
    [InlineData("show my files", NavigationRoute.Files)]
    [InlineData("open automations", NavigationRoute.Automations)]
    [InlineData("open assistant settings", NavigationRoute.Settings)]
    public async Task SpokenPhrasesAreRecognizedAsNavigationAndReachTheNavigator(
        string phrase,
        NavigationRoute expected)
    {
        using var harness = VoicePipelineHarness.Create();

        var recognized = await harness.Recognizer.RecognizeAsync(phrase);

        Assert.True(recognized.IsSuccess);
        Assert.Equal(AssistantIntent.Navigate, recognized.Value!.Intent);

        var result = await harness.Router.RouteAsync(recognized.Value!);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, Assert.Single(harness.Navigator.Requests));
    }

    [Fact]
    public async Task ARecognizedNavigationRequestCarriesTheSpokenDestination()
    {
        using var harness = VoicePipelineHarness.Create();

        var recognized = await harness.Recognizer.RecognizeAsync("open chat");

        Assert.Equal(
            expected: "chat",
            actual: recognized.Value!.GetParameter(VoiceCommand.NavigationDestinationParameter));
    }

    [Fact]
    public async Task TheDestinationSpokenIsTheRouteThatIsNavigated()
    {
        using var harness = VoicePipelineHarness.Create();

        // The handler must not pick a page of its own; it may only forward what was recognised.
        var command = harness.Command("open files", AssistantIntent.Navigate, parameters: new Dictionary<string, string>
        {
            [VoiceCommand.NavigationDestinationParameter] = "files"
        });

        var result = await harness.ExecutorFor(AssistantIntent.Navigate).ExecuteAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(NavigationRoute.Files, Assert.Single(harness.Navigator.Requests));
    }

    [Theory]
    [InlineData("open settings")]
    [InlineData("open windows settings")]
    public async Task OpeningWindowsSettingsStaysASystemCommand(string phrase)
    {
        using var harness = VoicePipelineHarness.Create();

        var recognized = await harness.Recognizer.RecognizeAsync(phrase);

        Assert.True(recognized.IsSuccess);
        Assert.Equal(AssistantIntent.OpenSettings, recognized.Value!.Intent);
        Assert.NotEqual(AssistantIntent.Navigate, recognized.Value!.Intent);

        var result = await harness.Router.RouteAsync(recognized.Value!);

        // It reaches the Windows Settings handler, which asks the shell to open a system
        // address, and it never asks for one of the assistant's own pages.
        Assert.True(result.IsSuccess);
        Assert.Empty(harness.Navigator.Requests);
        Assert.NotEmpty(harness.OpenedUris);
        Assert.All(harness.OpenedUris, uri => Assert.StartsWith("ms-settings:", uri.ToString()));
    }

    [Fact]
    public async Task ADestinationTheApplicationDoesNotHaveIsRefused()
    {
        using var harness = VoicePipelineHarness.Create(
            configureNavigator: navigator => navigator.AvailableRoutes.Remove(NavigationRoute.Automations));

        var command = harness.Command("open automations", AssistantIntent.Navigate, parameters: new Dictionary<string, string>
        {
            [VoiceCommand.NavigationDestinationParameter] = "automations"
        });

        var result = await harness.ExecutorFor(AssistantIntent.Navigate).ExecuteAsync(command);

        // The user is told plainly rather than being shown a different page.
        Assert.False(result.IsSuccess);
        Assert.Contains("available", result.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnknownDestinationIsRefusedRatherThanGuessedAt()
    {
        using var harness = VoicePipelineHarness.Create();

        var command = harness.Command("open the inbox", AssistantIntent.Navigate, parameters: new Dictionary<string, string>
        {
            [VoiceCommand.NavigationDestinationParameter] = "inbox"
        });

        var result = await harness.ExecutorFor(AssistantIntent.Navigate).ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Empty(harness.Navigator.Requests);
    }

    [Fact]
    public async Task AMissingDestinationIsRefused()
    {
        using var harness = VoicePipelineHarness.Create();

        var command = harness.Command("open something", AssistantIntent.Navigate);

        var result = await harness.ExecutorFor(AssistantIntent.Navigate).ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Empty(harness.Navigator.Requests);
    }

    [Fact]
    public async Task RepeatingTheCurrentPageIsAnsweredWithoutNavigatingAgain()
    {
        using var harness = VoicePipelineHarness.Create(
            configureNavigator: navigator => navigator.CurrentRoute = NavigationRoute.Chat);

        var command = harness.Command("open chat", AssistantIntent.Navigate, parameters: new Dictionary<string, string>
        {
            [VoiceCommand.NavigationDestinationParameter] = "chat"
        });

        var result = await harness.ExecutorFor(AssistantIntent.Navigate).ExecuteAsync(command);

        // Already being there is a fine answer, and it is answered without asking the shell to
        // push another copy of the same page onto its back stack.
        Assert.True(result.IsSuccess);
        Assert.Empty(harness.Navigator.Requests);
    }

    [Fact]
    public async Task NavigationNeverAsksForConsent()
    {
        using var harness = VoicePipelineHarness.Create(
            denied: [PermissionCapability.ApplicationLaunch, PermissionCapability.SystemControl]);

        var recognized = await harness.Recognizer.RecognizeAsync("open assistant settings");
        var result = await harness.Router.RouteAsync(recognized.Value!);

        // Changing page neither reads from the machine nor changes a system setting, so a
        // permission prompt here would only train someone to say yes to a harmless request.
        Assert.True(result.IsSuccess);
        Assert.Equal(NavigationRoute.Settings, Assert.Single(harness.Navigator.Requests));
    }

    [Fact]
    public async Task AHostWithNoUserInterfaceSaysThePageIsUnavailable()
    {
        // The Application layer has to be constructible without a window, so it registers a
        // placeholder navigator. It must answer honestly instead of claiming a page opened.
        using var harness = VoicePipelineHarness.Create(
            configureNavigator: navigator =>
            {
                navigator.AvailableRoutes.Clear();
                navigator.RefuseRequests = true;
            });

        var recognized = await harness.Recognizer.RecognizeAsync("open chat");
        var result = await harness.Router.RouteAsync(recognized.Value!);

        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.ResponseText));
    }
}
