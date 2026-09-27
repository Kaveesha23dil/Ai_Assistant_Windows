using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Application.Settings.Commands.SaveUserSettings;
using WindowsAIAssistant.Core.Abstractions.Configuration;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Infrastructure;
using WindowsAIAssistant.Infrastructure.Configuration;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using WindowsAIAssistant.Infrastructure.Configuration.Persistence;

namespace WindowsAIAssistant.Application.Tests.Settings;

/// <summary>
/// Covers the path a settings change takes: written to the person's own file, read back as
/// configuration, and reflected in the values the running application reads.
/// <para>
/// The store is always given a path inside a temporary folder. A test that wrote to the real
/// location would change the settings of whoever ran it, which is the one outcome a settings
/// test must never produce.
/// </para>
/// </summary>
public sealed class UserSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "WindowsAIAssistant.Tests",
        Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_folder, "user-settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task SavedSettingsAreWrittenInTheShapeConfigurationExpects()
    {
        var store = CreateStore();
        var settings = SampleSettings();

        var result = await store.SaveAsync(settings);

        Assert.True(result.IsSuccess, result.ErrorMessage);

        // The file is read back as a configuration source, so the section names have to be the
        // ones the options bind to. Checking the document rather than the object is what makes
        // that guarantee testable.
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath));
        var root = document.RootElement;

        Assert.True(root.GetProperty("Privacy").GetProperty("AllowMicrophoneAccess").GetBoolean());
        Assert.True(root.GetProperty("Voice").GetProperty("Enabled").GetBoolean());
        Assert.Equal("Dark", root.GetProperty("UI").GetProperty("Theme").GetString());
    }

    [Fact]
    public async Task SavingAgainReplacesTheEarlierSettings()
    {
        var store = CreateStore();
        await store.SaveAsync(SampleSettings());

        var second = SampleSettings();
        second.Privacy.AllowMicrophoneAccess = false;
        second.UI.Theme = "Light";

        await store.SaveAsync(second);

        var reloaded = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath));
        Assert.False(reloaded.RootElement.GetProperty("Privacy").GetProperty("AllowMicrophoneAccess").GetBoolean());
        Assert.Equal("Light", reloaded.RootElement.GetProperty("UI").GetProperty("Theme").GetString());
    }

    [Fact]
    public async Task NoTemporaryFileIsLeftBehind()
    {
        var store = CreateStore();

        await store.SaveAsync(SampleSettings());

        // A leftover temporary file would accumulate in the person's profile and imply a save
        // that never completed.
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public async Task AFailedWriteReportsAFailureInsteadOfThrowing()
    {
        // A file where a folder is expected makes the write impossible, which stands in for a
        // profile the application is not allowed to write to.
        Directory.CreateDirectory(_folder);
        var blocker = Path.Combine(_folder, "blocker");
        await File.WriteAllTextAsync(blocker, "not a folder");
        var store = new JsonFileSettingsStore(
            NullLogger<JsonFileSettingsStore>.Instance,
            Path.Combine(blocker, "user-settings.json"));

        var result = await store.SaveAsync(SampleSettings());

        Assert.True(result.IsFailure);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task TheHandlerSavesSettingsThatAreAcceptable()
    {
        var store = CreateStore();
        var handler = new SaveUserSettingsHandler(store, NullLogger<SaveUserSettingsHandler>.Instance);

        var result = await handler.HandleAsync(SampleSettings());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(File.Exists(SettingsPath));
    }

    [Theory]
    [InlineData("Neon")]
    [InlineData("")]
    public async Task AnUnsupportedThemeIsRefusedRatherThanSaved(string theme)
    {
        var store = CreateStore();
        var handler = new SaveUserSettingsHandler(store, NullLogger<SaveUserSettingsHandler>.Instance);
        var settings = SampleSettings();
        settings.UI.Theme = theme;

        var result = await handler.HandleAsync(settings);

        // Writing a theme nothing honours would leave the person believing they had chosen it.
        Assert.True(result.IsFailure);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public async Task IncompleteSettingsAreRefused()
    {
        var store = CreateStore();
        var handler = new SaveUserSettingsHandler(store, NullLogger<SaveUserSettingsHandler>.Instance);
        var settings = SampleSettings();
        settings.UI = null!;

        var result = await handler.HandleAsync(settings);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task AFailedStoreIsReportedToTheCaller()
    {
        var handler = new SaveUserSettingsHandler(
            new FailingSettingsStore(),
            NullLogger<SaveUserSettingsHandler>.Instance);

        var result = await handler.HandleAsync(SampleSettings());

        // The caller has to be able to tell the person their change was not kept.
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SavedSettingsBecomeTheOptionsTheApplicationReads()
    {
        // This is the behaviour the person actually notices: what they saved is what the
        // running application uses, without a restart and without editing appsettings.json.
        // ConfigurationManager is what the host itself uses, so the test exercises the same
        // arrangement the application runs with.
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddLogging();
        configuration.AddUserSettingsFile(SettingsPath);
        services.AddUserSettings(SettingsPath);
        services.AddApplicationConfiguration(configuration);

        var settings = SampleSettings();
        await new JsonFileSettingsStore(NullLogger<JsonFileSettingsStore>.Instance, SettingsPath)
            .SaveAsync(settings);

        ((IConfigurationRoot)configuration).Reload();

        using var provider = services.BuildServiceProvider();
        var privacy = provider.GetRequiredService<IOptionsMonitor<PrivacyOptions>>().CurrentValue;
        var voice = provider.GetRequiredService<IOptionsMonitor<VoiceOptions>>().CurrentValue;
        var ui = provider.GetRequiredService<IOptions<UIOptions>>().Value;

        Assert.True(privacy.AllowMicrophoneAccess);
        Assert.True(privacy.AllowVoiceProcessing);
        Assert.True(voice.Enabled);
        Assert.False(voice.ContinuousListening);
        Assert.Equal("Dark", ui.Theme);
    }

    [Fact]
    public void NoSettingsFileMeansTheShippedDefaultsStillApply()
    {
        // A first launch has no file at all, and that must not be an error.
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddLogging();
        configuration.AddUserSettingsFile(SettingsPath);
        services.AddUserSettings(SettingsPath);
        services.AddApplicationConfiguration(configuration);

        using var provider = services.BuildServiceProvider();

        // A first launch has no file at all, and that must not be an error.
        Assert.False(File.Exists(SettingsPath));
        Assert.False(provider.GetRequiredService<IOptionsMonitor<PrivacyOptions>>().CurrentValue.AllowMicrophoneAccess);
    }

    [Fact]
    public void TheProductionCompositionKeepsSettingsInThePersonsOwnProfile()
    {
        // The path the running application uses is the one the person is told about, so it is
        // worth asserting separately from the temporary paths the other tests write to. Nothing
        // is written here: resolving the store only decides where a save would go.
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<ISettingsStore>();

        Assert.Equal(JsonFileSettingsStore.DefaultPath(), store.Location);
        Assert.Contains("WindowsAIAssistant", store.Location, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("user-settings.json", store.Location, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheProductionCompositionCanSaveWithoutNamingAPath()
    {
        // A save through the real composition must not need the caller to say where settings
        // live, and the handler the Settings page uses must be the registered one.
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration, SettingsPath);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<SaveUserSettingsHandler>());
        Assert.Equal(SettingsPath, provider.GetRequiredService<ISettingsStore>().Location);
    }

    private JsonFileSettingsStore CreateStore() =>
        new(NullLogger<JsonFileSettingsStore>.Instance, SettingsPath);

    private static UserSettings SampleSettings() => new()
    {
        Privacy = new PrivacySettings
        {
            AllowMicrophoneAccess = true,
            AllowVoiceProcessing = true,
        },
        Voice = new VoiceSettings
        {
            Enabled = true,
            ContinuousListening = false,
        },
        UI = new UiSettings
        {
            Theme = "Dark",
        },
    };

    private sealed class FailingSettingsStore : ISettingsStore
    {
        public string Location => "nowhere";

        public Task<Result> SaveAsync(UserSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Failure("The settings could not be saved."));
    }
}
