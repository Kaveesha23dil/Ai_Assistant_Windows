using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WindowsAIAssistant.Application;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Application.Voice;
using WindowsAIAssistant.Application.Voice.Services;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Time;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Abstractions.Web;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Core.Models.Voice;
using WindowsAIAssistant.Infrastructure;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Application.Tests.Helpers;

/// <summary>
/// Builds the real voice pipeline with only the operating-system boundary replaced.
/// <para>
/// The intent recognizer, router, permission gate, session and every command handler are the
/// production implementations, so these tests exercise the same wiring the application uses.
/// Only the services that touch a real device are faked, which keeps the tests hermetic
/// without also mocking away the behaviour under test.
/// </para>
/// </summary>
public sealed class VoicePipelineHarness : IDisposable
{
    private readonly ServiceProvider _provider;

    private VoicePipelineHarness(
        ServiceProvider provider,
        FakePermissionService permissions,
        FakeSpeechRecognitionService recognition,
        FakeSpeechSynthesisService synthesis,
        FakeVolumeService volume,
        FakeUriLauncherService uriLauncher,
        FakeBatteryService battery,
        FakeApplicationResolver resolver,
        List<Uri> openedUris)
    {
        _provider = provider;
        Permissions = permissions;
        Recognition = recognition;
        Synthesis = synthesis;
        Volume = volume;
        UriLauncher = uriLauncher;
        Battery = battery;
        Resolver = resolver;
        OpenedUris = openedUris;
    }

    public FakePermissionService Permissions { get; }

    public FakeSpeechRecognitionService Recognition { get; }

    public FakeSpeechSynthesisService Synthesis { get; }

    public FakeVolumeService Volume { get; }

    public FakeBatteryService Battery { get; }

    /// <summary>
    /// Gets the resolver that turns a spoken name into an allow-listed target.
    /// <para>
    /// The launcher is deliberately left as the real one. It is the security boundary for
    /// application launching, so a fake that accepted every name would let a test pass while
    /// the real allow-list did nothing.
    /// </para>
    /// </summary>
    public FakeApplicationResolver Resolver { get; }

    public FakeUriLauncherService UriLauncher { get; }

    /// <summary>Gets every address the pipeline asked the shell to open.</summary>
    public List<Uri> OpenedUris { get; }

    public IVoiceAssistantService Assistant => _provider.GetRequiredService<IVoiceAssistantService>();

    public IIntentRecognizer Recognizer => _provider.GetRequiredService<IIntentRecognizer>();

    public ICommandRouter Router => _provider.GetRequiredService<ICommandRouter>();

    public IWakeWordService WakeWord => _provider.GetRequiredService<IWakeWordService>();

    /// <summary>
    /// Gets the concrete history service. The interface hides the switch, but a test has to be
    /// able to turn recording on to prove what is written when it is on.
    /// </summary>
    public VoiceCommandHistory History => _provider.GetRequiredService<VoiceCommandHistory>();

    public IAssistantActionRegistry Registry => _provider.GetRequiredService<IAssistantActionRegistry>();

    public IAssistantActionExecutor ExecutorFor(AssistantIntent intent)
    {
        Assert.True(
            Registry.TryGetExecutor(intent, out var executor) && executor is not null,
            $"No executor is registered for {intent}.");

        return executor!;
    }

    public VoiceCommand Command(
        string text,
        AssistantIntent intent,
        double confidence = 1.0,
        ActionSafetyLevel safety = ActionSafetyLevel.Safe,
        bool requiresConfirmation = false,
        IReadOnlyDictionary<string, string>? parameters = null) =>
        VoiceCommand.Create(
            text,
            intent,
            parameters,
            confidence,
            safety,
            requiresConfirmation);

    /// <param name="configurePolicy">
    /// Optionally returns a modified policy. The policy is a record, so a caller overrides one
    /// setting with <c>policy with { ... }</c> and leaves the rest at its safe default.
    /// </param>
    public static VoicePipelineHarness Create(
        Func<VoiceIntentPolicy, VoiceIntentPolicy>? configurePolicy = null,
        params PermissionCapability[] denied)    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        var permissions = new FakePermissionService();
        foreach (var capability in denied)
        {
            permissions.Denied.Add(capability);
        }

        var recognition = new FakeSpeechRecognitionService();
        var synthesis = new FakeSpeechSynthesisService();
        var volume = new FakeVolumeService();
        var openedUris = new List<Uri>();
        var uriLauncher = new FakeUriLauncherService(openedUris);
        var knownFolders = new FakeKnownFolderService();
        var settings = new FakeWindowsSettingsService();
        var applicationResolver = new FakeApplicationResolver("notepad", "calculator");
        var battery = new FakeBatteryService();
        var driveSpace = new FakeDriveSpaceService();
        var screenshot = new FakeScreenshotService();
        var time = new FakeDateTimeProvider(new DateTimeOffset(2026, 3, 14, 9, 30, 0, TimeSpan.Zero));
        var webProviders = new FakeWebSearchProviders();

        // Registered after AddInfrastructure so these win. Anything not replaced here would
        // reach real hardware, so the list is deliberately explicit and complete.
        services.AddSingleton<IPermissionService>(permissions);
        services.AddSingleton<ISpeechRecognitionService>(recognition);
        services.AddSingleton<ISpeechSynthesisService>(synthesis);
        services.AddSingleton<IWakeWordService, FakeWakeWordService>();
        services.AddSingleton<IVolumeService>(volume);
        services.AddSingleton<IUriLauncherService>(uriLauncher);
        services.AddSingleton<IKnownFolderService>(knownFolders);
        services.AddSingleton<IWindowsSettingsService>(settings);
        services.AddSingleton<IApplicationResolver>(applicationResolver);
        services.AddSingleton<IBatteryService>(battery);
        services.AddSingleton<IDriveSpaceService>(driveSpace);
        services.AddSingleton<IScreenshotService>(screenshot);
        services.AddSingleton<IDateTimeProvider>(time);

        // A collection of providers cannot be shadowed by adding a later one, so the real
        // registrations are removed rather than merely outranked.
        foreach (var descriptor in services
            .Where(descriptor => descriptor.ServiceType == typeof(IWebSearchProvider))
            .ToList())
        {
            services.Remove(descriptor);
        }

        services.AddSingleton<IWebSearchProvider>(webProviders.Default);
        services.AddSingleton<IWebSearchProvider>(webProviders.YouTube);

        var policy = new VoiceIntentPolicy();
        if (configurePolicy is not null)
        {
            policy = configurePolicy(policy) ?? policy;
        }
        services.AddSingleton(policy);

        // The application registers the history only behind its interface, but a test has to
        // reach the concrete service to flip the recording switch. This aliases the very same
        // instance rather than creating a second one.
        services.AddSingleton(sp => (VoiceCommandHistory)sp.GetRequiredService<IVoiceCommandHistory>());

        return new VoicePipelineHarness(
            services.BuildServiceProvider(),
            permissions,
            recognition,
            synthesis,
            volume,
            uriLauncher,
            battery,
            applicationResolver,
            openedUris);
    }

    public void Dispose() => _provider.Dispose();
}

/// <summary>Opens nothing; records the address so a test can assert what was requested.</summary>
public sealed class FakeUriLauncherService(List<Uri> opened) : IUriLauncherService
{
    public Result Result { get; set; } = Result.Success();

    public Task<Result> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Result.IsFailure)
        {
            return Task.FromResult(Result);
        }

        opened.Add(uri);
        return Task.FromResult(Result.Success());
    }
}

/// <summary>Maps folder names to predictable paths.</summary>
public sealed class FakeKnownFolderService : IKnownFolderService
{
    public Dictionary<KnownFolderKind, string> Paths { get; } = new()
    {
        [KnownFolderKind.Downloads] = @"C:\Users\test\Downloads",
        [KnownFolderKind.Documents] = @"C:\Users\test\Documents",
        [KnownFolderKind.Pictures] = @"C:\Users\test\Pictures",
    };

    public Task<Result<string>> GetPathAsync(
        KnownFolderKind folder,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Paths.TryGetValue(folder, out var path)
                ? Result<string>.Success(path)
                : Result<string>.Failure($"The {folder} folder is not available on this system."));
    }
}

/// <summary>Resolves the settings pages the assistant is allowed to open.</summary>
public sealed class FakeWindowsSettingsService : IWindowsSettingsService
{
    public IReadOnlyCollection<string> SupportedPages { get; } =
        ["wifi", "bluetooth", "display", "sound", "power"];

    public Result<Uri> ResolvePageUri(string pageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageName);

        return SupportedPages.Contains(pageName, StringComparer.OrdinalIgnoreCase)
            ? Result<Uri>.Success(new Uri($"ms-settings:{pageName}"))
            : Result<Uri>.Failure($"The settings page '{pageName}' is not supported.");
    }
}

/// <summary>Reports a fixed charge so battery answers are predictable.</summary>
public sealed class FakeBatteryService : IBatteryService
{
    public bool IsBatteryPresent => true;

    public int Percentage { get; set; } = 64;

    public bool IsCharging { get; set; }

    public Task<Result<BatteryStatus>> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<BatteryStatus>.Success(
            new BatteryStatus(Percentage, IsCharging, true, null)));
}

/// <summary>Reports a fixed drive so storage answers are predictable.</summary>
public sealed class FakeDriveSpaceService : IDriveSpaceService
{
    public Task<Result<DriveSpace>> GetSystemDriveSpaceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<DriveSpace>.Success(
            new DriveSpace("C:", 512L * 1024 * 1024 * 1024, 128L * 1024 * 1024 * 1024)));
}

/// <summary>Writes nothing; reports a path a test can assert.</summary>
public sealed class FakeScreenshotService : IScreenshotService
{
    public Task<Result<ScreenshotResult>> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<ScreenshotResult>.Success(
            new ScreenshotResult(@"C:\Users\test\Pictures\Screenshot.png", 2048, DateTimeOffset.UtcNow)));
}

/// <summary>Two providers, so the handler's provider selection can be asserted.</summary>
public sealed class FakeWebSearchProviders
{
    public FakeWebSearchProvider Default { get; } = new("web", WebSearchProviderType.Default, true);

    public FakeWebSearchProvider YouTube { get; } = new("youtube", WebSearchProviderType.YouTube, false);
}

/// <summary>Builds a search address without contacting the network.</summary>
public sealed class FakeWebSearchProvider(string name, WebSearchProviderType type, bool isDefault)
    : IWebSearchProvider
{
    public string Name { get; } = name;

    public WebSearchProviderType ProviderType { get; } = type;

    public bool IsDefault { get; } = isDefault;

    public Result<Uri> BuildSearchUri(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result<Uri>.Failure("A search needs something to search for.");
        }

        return Result<Uri>.Success(new Uri($"https://example.invalid/{Name}?q={Uri.EscapeDataString(query)}"));
    }
}

/// <summary>No wake engine, matching the shipped behaviour.</summary>
public sealed class FakeWakeWordService : IWakeWordService
{
    public bool IsEnabled { get; private set; }

    public IReadOnlyCollection<string> WakePhrases { get; } = ["hey assistant"];

    public bool IsAvailable => false;

    public event EventHandler<WakeWordMatch>? WakeWordDetected;

    /// <summary>Simulates the engine hearing a wake phrase, which the real one never can.</summary>
    public void RaiseWakeWord(string phrase) =>
        WakeWordDetected?.Invoke(this, new WakeWordMatch(phrase, 1.0, DateTimeOffset.UtcNow));

    public Task<Result> StartAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure(
            "Wake word detection is unavailable because no local engine is installed."));

    public Task<Result> StopAsync(CancellationToken cancellationToken = default)
    {
        IsEnabled = false;
        return Task.FromResult(Result.Success());
    }
}
