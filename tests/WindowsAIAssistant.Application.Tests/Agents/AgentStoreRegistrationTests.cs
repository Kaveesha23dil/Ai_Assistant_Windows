using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Application;
using WindowsAIAssistant.Application.Agents;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure;
using WindowsAIAssistant.Infrastructure.Agents;
using WindowsAIAssistant.Infrastructure.Agents.Configuration;

namespace WindowsAIAssistant.Application.Tests.Agents;

/// <summary>
/// Checks that the container gives the agent the stores and the presentation mode the
/// configuration asked for.
/// <para>
/// The Application layer registers in-memory stores so that it can be reasoned about without a
/// database, and the Infrastructure layer replaces them with SQLite. That arrangement has two ways
/// to go wrong which no unit test of either store would catch: both registrations surviving, so a
/// run writes to memory while a reader reads from a file, and the replacement happening when it
/// should not, so somebody who asked not to be remembered still is. The assertions are about what
/// the container resolves, because the container is the only place where the two are brought
/// together.
/// </para>
/// </summary>
public sealed class AgentStoreRegistrationTests
{
    [Fact]
    public void TheDefaultIsAPersistentStoreRatherThanAnInMemoryOne()
    {
        using var provider = Build();

        // The default matters: somebody who has never opened the settings page still expects the
        // agent to be the same agent tomorrow.
        Assert.IsType<SqliteAgentMemoryStore>(provider.GetRequiredService<IAgentMemoryStore>());
        Assert.IsType<SqliteAgentActivityStore>(provider.GetRequiredService<IAgentActivityStore>());
    }

    [Fact]
    public void OnlyOneImplementationOfEachStoreIsRegistered()
    {
        using var provider = Build();

        // Both the in-memory fallback and the SQLite store being resolvable would mean the
        // timeline somebody is reading is not necessarily the one a run wrote to.
        Assert.Single(provider.GetServices<IAgentMemoryStore>());
        Assert.Single(provider.GetServices<IAgentActivityStore>());
    }

    [Fact]
    public void TheApplicationLayerOnItsOwnUsesInMemoryStores()
    {
        // The Application layer must not depend on a database existing, so this is what it gives
        // on its own. It is also the reason the Infrastructure layer has to displace it explicitly.
        var services = new ServiceCollection();

        services.AddApplication();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryAgentMemoryStore>(provider.GetRequiredService<IAgentMemoryStore>());
        Assert.IsType<InMemoryAgentActivityStore>(provider.GetRequiredService<IAgentActivityStore>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Persistent")]
    [InlineData("persistent")]
    public void APersistentConfigurationResolvesTheFileBackedStores(string? configured)
    {
        using var provider = Build(configured is { } value
            ? new Dictionary<string, string?> { ["Agent:Persistence"] = value }
            : null);

        Assert.IsType<SqliteAgentMemoryStore>(provider.GetRequiredService<IAgentMemoryStore>());
    }

    [Theory]
    [InlineData("SessionOnly")]
    [InlineData("sessiononly")]
    [InlineData("SESSIONONLY")]
    public void ASessionOnlyConfigurationKeepsTheInMemoryStores(string configured)
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Agent:Persistence"] = configured,
        });

        // This is the promise behind "remember nothing": with the in-memory store selected, a
        // preference written now is not in the file afterwards, and not there after a restart.
        Assert.IsType<InMemoryAgentMemoryStore>(provider.GetRequiredService<IAgentMemoryStore>());
        Assert.IsType<InMemoryAgentActivityStore>(provider.GetRequiredService<IAgentActivityStore>());
        Assert.Single(provider.GetServices<IAgentMemoryStore>());
    }

    [Fact]
    public void ASessionOnlyConfigurationStillLeavesTheStoreServiceResolvable()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Agent:Persistence"] = "SessionOnly",
        });

        // The agent takes its stores as constructor arguments. A container that could not resolve
        // the interface at all would fail at startup, which is worse than remembering nothing.
        Assert.NotNull(provider.GetRequiredService<IAgentMemoryStore>());
    }

    [Fact]
    public void TheFileNameAndRetentionCountComeFromConfiguration()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Agent:DatabaseFileName"] = "agent-store.db",
            ["Agent:ActivityRetentionCount"] = "25",
        });

        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>().Value;

        Assert.Equal("agent-store.db", options.DatabaseFileName);
        Assert.Equal(25, options.ActivityRetentionCount);
    }

    [Fact]
    public void TheStoreLivesUnderLocalApplicationDataRatherThanBesideTheApplication()
    {
        using var provider = Build();

        var path = provider.GetRequiredService<AgentDatabase>().DatabasePath;

        // A file written next to the executable would be somewhere a reinstall can delete without
        // being asked, and somewhere a portable copy would write to its own install folder.
        var expectedRoot = Path.Combine(
            Environment.GetEnvironmentVariable("LOCALAPPDATA")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAIAssistant");

        Assert.Equal(
            Path.GetFullPath(expectedRoot),
            Path.GetFullPath(Path.GetDirectoryName(path)!));
        Assert.Equal("agent.db", Path.GetFileName(path));
    }

    [Fact]
    public void ResolvingTheStoreDoesNotCreateTheFile()
    {
        // Built against a path that cannot exist yet, so this cannot pass by finding a file left
        // behind by an earlier run. Nothing opens the database until something asks it for
        // something, so a person who never uses the agent does not find a file of activity waiting.
        var path = Path.Combine(
            Path.GetTempPath(),
            "waa-agent-registration-" + Guid.NewGuid().ToString("N"),
            "agent.db");

        var services = new ServiceCollection();
        services.AddLogging();

        var database = new AgentDatabase(
            Options.Create(new AgentOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentDatabase>.Instance,
            path);

        services.AddSingleton(database);
        services.AddSingleton<IAgentMemoryStore, SqliteAgentMemoryStore>();

        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IAgentMemoryStore>();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TheConfiguredPresentationModeIsWhatTheCoordinatorResolves()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Agent:PresentationMode"] = "Competition",
        });

        var settings = provider.GetRequiredService<IAgentPresentationSettings>();

        // The Application layer registers a standard default, so without the Infrastructure
        // replacement the competition mode in configuration would silently do nothing.
        Assert.IsType<ConfiguredAgentPresentationSettings>(settings);
        Assert.Equal(AgentPresentationMode.Competition, settings.Mode);
    }

    [Fact]
    public void TheDefaultPresentationModeIsStandard()
    {
        using var provider = Build();

        Assert.Equal(AgentPresentationMode.Standard, provider.GetRequiredService<IAgentPresentationSettings>().Mode);
    }

    [Fact]
    public void AnUnsetPresentationModeDoesNotBecomeCompetition()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Agent:PresentationMode"] = string.Empty,
        });

        // An unreadable value must not resolve to the mode that changes how the application looks,
        // because the failure would be cosmetic rather than obvious.
        Assert.NotEqual(AgentPresentationMode.Competition, provider.GetRequiredService<IAgentPresentationSettings>().Mode);
    }

    [Fact]
    public void OnlyOneImplementationOfThePresentationSettingsIsRegistered()
    {
        using var provider = Build();

        Assert.Single(provider.GetServices<IAgentPresentationSettings>());
    }

    [Fact]
    public void ThePresentationOnItsOwnAlwaysReportsStandardMode()
    {
        // The Application layer's policy object is asked what the mode is rather than told, so a
        // caller cannot pass in a mode the policy does not support.
        var presentation = new AgentPresentation();

        Assert.Equal(AgentPresentationMode.Standard, presentation.Mode);
    }

    [Fact]
    public void TheRemovedBooleanOptionIsNoLongerPartOfTheContract()
    {
        // The old option was a bool called Persist. A configuration file that still carries it
        // binds to nothing, and leaving the property would mean it looked honoured while doing
        // nothing - the failure mode where a privacy switch lies.
        Assert.Null(typeof(AgentOptions).GetProperty("Persist", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(typeof(AgentOptions).GetProperty("Persistence"));
    }

    private static ServiceProvider Build(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        services.AddApplication();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }
}
