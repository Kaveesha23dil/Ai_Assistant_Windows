using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.App.Services.Navigation;
using WindowsAIAssistant.App.Views.Pages;
using WindowsAIAssistant.Core.Abstractions.Navigation;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using NavigationService = WindowsAIAssistant.App.Services.Navigation.NavigationService;

namespace WindowsAIAssistant.Application.Tests.Navigation;

/// <summary>
/// Checks the route-to-page table and the decisions that are made from it.
/// <para>
/// These are the parts of navigation that can be settled without a window. The frame itself is
/// deliberately not under test: showing a page needs a live XAML tree, and a test bound to one
/// would fail when the toolkit changes rather than when the mapping is wrong. Everything
/// asserted here is a decision made before the frame is touched: which page a route means,
/// whether a route is offered at all, and which route the application opens with.
/// </para>
/// <para>
/// The table is asserted against the real pages rather than against stand-ins, because being
/// the single place where a route is tied to a page is the entire purpose of the registry. A
/// second copy of that mapping inside a test would let the two drift apart unnoticed.
/// </para>
/// </summary>
public sealed class NavigationRouteRegistryTests
{
    private static NavigationRouteRegistry Registry => NavigationRouteRegistry.CreateDefault();

    [Fact]
    public void HomeRouteResolvesToTheHomePage()
    {
        Assert.True(Registry.TryResolve(NavigationRoute.Home, out var pageType));
        Assert.Equal(typeof(HomePage), pageType);
    }

    [Fact]
    public void ChatRouteResolvesToTheChatPage()
    {
        Assert.True(Registry.TryResolve(NavigationRoute.Chat, out var pageType));
        Assert.Equal(typeof(ChatPage), pageType);
    }

    [Fact]
    public void FilesRouteResolvesToTheFilesPage()
    {
        Assert.True(Registry.TryResolve(NavigationRoute.Files, out var pageType));
        Assert.Equal(typeof(FilesPage), pageType);
    }

    [Fact]
    public void AutomationsRouteResolvesToTheAutomationsPage()
    {
        Assert.True(Registry.TryResolve(NavigationRoute.Automations, out var pageType));
        Assert.Equal(typeof(AutomationsPage), pageType);
    }

    [Fact]
    public void SettingsRouteResolvesToTheSettingsPage()
    {
        Assert.True(Registry.TryResolve(NavigationRoute.Settings, out var pageType));
        Assert.Equal(typeof(SettingsPage), pageType);
    }

    [Fact]
    public void EveryRouteIsAlsoMappableBackFromItsPage()
    {
        // The reverse index is what lets the sidebar highlight the item belonging to a page that
        // was shown from anywhere, so both directions are asserted over the whole table.
        var registry = Registry;

        // Counted against the enum rather than hard-coded, so that adding a destination has to
        // add a page here too. A fixed number would let a route exist with no page and the loop
        // below would pass without ever looking at it.
        Assert.Equal(Enum.GetValues<NavigationRoute>().Length, registry.Routes.Count);

        foreach (var route in registry.Routes)
        {
            Assert.True(registry.TryResolve(route, out var pageType));
            Assert.True(registry.TryResolveRoute(pageType, out var resolved));
            Assert.Equal(route, resolved);
        }
    }

    [Fact]
    public void AnUndefinedRouteIsRejectedRatherThanGuessedAt()
    {
        // A route the table does not know is the shape of a stale voice phrase or a page nobody
        // registered. It has to fail rather than fall back to Home, or a request would be
        // silently answered with a different page.
        var route = (NavigationRoute)999;

        Assert.False(Registry.Contains(route));
        Assert.False(Registry.TryResolve(route, out var pageType));
        Assert.Null(pageType);
    }

    [Fact]
    public void AnEmptyTableRefusesEveryRoute()
    {
        var empty = NavigationRouteRegistry.CreateEmpty();

        Assert.Empty(empty.Routes);
        Assert.False(empty.Contains(NavigationRoute.Home));
        Assert.False(empty.TryResolve(NavigationRoute.Home, out _));
    }

    [Theory]
    [InlineData("Home", NavigationRoute.Home)]
    [InlineData("Chat", NavigationRoute.Chat)]
    [InlineData("  chat ", NavigationRoute.Chat)]
    [InlineData("Files", NavigationRoute.Files)]
    [InlineData("Automations", NavigationRoute.Automations)]
    [InlineData("Settings", NavigationRoute.Settings)]
    [InlineData("assistant settings", NavigationRoute.Settings)]
    [InlineData("app settings", NavigationRoute.Settings)]
    public void NavigationItemTagsAreReadByTheSharedParser(string tag, NavigationRoute expected)
    {
        // The sidebar, the configuration, and the voice layer all use the same vocabulary, so a
        // tag that stops being understood is a defect in one place rather than three.
        Assert.True(NavigationRouteExtensions.TryParse(tag, out var route));
        Assert.Equal(expected, route);
        Assert.True(Registry.Contains(route));
    }

    [Theory]
    [InlineData("inbox")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownTagIsRefused(string? tag) =>
        Assert.False(NavigationRouteExtensions.TryParse(tag, out _));

    [Fact]
    public void EveryRouteHasADisplayName()
    {
        foreach (var route in Registry.Routes)
        {
            Assert.False(string.IsNullOrWhiteSpace(route.ToDisplayName()));
        }

        Assert.Equal("Settings", NavigationRoute.Settings.ToDisplayName());
    }

    [Fact]
    public void RegisteringTheSameRouteTwiceIsRejected()
    {
        // Two pages for one route would make the outcome depend on registration order, so the
        // table refuses to be built rather than quietly picking one.
        Assert.Throws<ArgumentException>(() => NavigationRouteRegistry.Create(
        [
            new NavigationRegistration(NavigationRoute.Chat, typeof(ChatPage)),
            new NavigationRegistration(NavigationRoute.Chat, typeof(FilesPage))
        ]));
    }

    [Fact]
    public void RegisteringOnePageForTwoRoutesIsRejected()
    {
        Assert.Throws<ArgumentException>(() => NavigationRouteRegistry.Create(
        [
            new NavigationRegistration(NavigationRoute.Chat, typeof(ChatPage)),
            new NavigationRegistration(NavigationRoute.Files, typeof(ChatPage))
        ]));
    }

    [Fact]
    public void RegisteringSomethingThatIsNotAPageIsRejected()
    {
        Assert.Throws<ArgumentException>(() => NavigationRouteRegistry.Create(
        [
            new NavigationRegistration(NavigationRoute.Chat, typeof(string))
        ]));
    }

    [Fact]
    public void AConfiguredDefaultRouteIsHonoured()
    {
        Assert.Equal(NavigationRoute.Chat, Registry.ResolveDefault("Chat"));
        Assert.Equal(NavigationRoute.Automations, Registry.ResolveDefault("automations"));
    }

    [Theory]
    [InlineData("Inbox")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnusableConfiguredDefaultFallsBackToHome(string? configured)
    {
        // A typo in configuration has to produce a working application, not a blank one.
        Assert.Equal(NavigationRoute.Home, Registry.ResolveDefault(configured));
    }

    [Fact]
    public void AConfiguredDefaultThatIsNotRegisteredFallsBackToHome()
    {
        var partial = NavigationRouteRegistry.Create(
        [
            new NavigationRegistration(NavigationRoute.Home, typeof(HomePage)),
            new NavigationRegistration(NavigationRoute.Chat, typeof(ChatPage))
        ]);

        // Settings parses as a real route but has no page here, so honouring it would leave the
        // application with nothing to show.
        Assert.Equal(NavigationRoute.Home, partial.ResolveDefault("Settings"));
    }

    [Fact]
    public void ATableWithoutHomeStillResolvesToSomethingShowable()
    {
        var partial = NavigationRouteRegistry.Create(
        [
            new NavigationRegistration(NavigationRoute.Chat, typeof(ChatPage))
        ]);

        Assert.True(partial.Contains(partial.ResolveDefault("not a page")));
    }

    [Fact]
    public void TheServiceHonoursAValidConfiguredDefault()
    {
        WithService("Files", navigation =>
        {
            Assert.Equal(NavigationRoute.Files, navigation.DefaultRoute);
            Assert.Equal(NavigationRoute.Files, NavigationRouteRegistry.CreateDefault().ResolveDefault("Files"));
        });
    }

    [Fact]
    public void TheServiceFallsBackToHomeForAnUnusableConfiguredDefault()
    {
        // The service is what the window asks for the opening route, so the fallback has to
        // happen here rather than only in the registry.
        WithService("Nonsense", navigation =>
            Assert.Equal(NavigationRoute.Home, navigation.DefaultRoute));
    }

    [Fact]
    public void TheThreeRegistrationsResolveToOneSharedService()
    {
        // The sidebar and the voice layer must be talking about the same page. If any of these
        // resolved a second service, each would hold its own idea of the current route and the
        // selection would drift from the page on screen.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_ => NavigationRouteRegistry.CreateDefault());
        services.AddSingleton(Options.Create(new UIOptions()));
        services.AddSingleton<NavigationService>();
        services.AddSingleton<INavigationService>(provider => provider.GetRequiredService<NavigationService>());
        services.AddSingleton<IApplicationNavigator>(provider => provider.GetRequiredService<NavigationService>());

        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<NavigationService>();

        Assert.Same(service, provider.GetRequiredService<INavigationService>());
        Assert.Same(service, provider.GetRequiredService<IApplicationNavigator>());
        Assert.Same(
            provider.GetRequiredService<INavigationService>(),
            provider.GetRequiredService<INavigationService>());
    }

    private static void WithService(string configuredDefaultPage, Action<NavigationService> assert)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_ => NavigationRouteRegistry.CreateDefault());
        services.AddSingleton(Options.Create(new UIOptions { DefaultPage = configuredDefaultPage }));
        services.AddSingleton<NavigationService>();

        using var provider = services.BuildServiceProvider();
        assert(provider.GetRequiredService<NavigationService>());
    }
}
