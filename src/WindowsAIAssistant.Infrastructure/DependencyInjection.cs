using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Abstractions.Embeddings;
using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Abstractions.Reports;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Storage;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Time;
using WindowsAIAssistant.Core.Abstractions.Vision;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Abstractions.Web;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.AI;
using WindowsAIAssistant.Infrastructure.Agents;
using WindowsAIAssistant.Infrastructure.Agents.Configuration;
using WindowsAIAssistant.Infrastructure.Configuration;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using WindowsAIAssistant.Infrastructure.Configuration.Validation;
using WindowsAIAssistant.Infrastructure.Development;
using WindowsAIAssistant.Infrastructure.Documents;
using WindowsAIAssistant.Infrastructure.Documents.Excel;
using WindowsAIAssistant.Infrastructure.Documents.Pdf;
using WindowsAIAssistant.Infrastructure.Documents.PowerPoint;
using WindowsAIAssistant.Infrastructure.Documents.Text;
using WindowsAIAssistant.Infrastructure.Documents.Word;
using WindowsAIAssistant.Infrastructure.Embeddings;
using WindowsAIAssistant.Infrastructure.Knowledge;
using WindowsAIAssistant.Infrastructure.Reports;
using WindowsAIAssistant.Infrastructure.Vision;
using WindowsAIAssistant.Infrastructure.Vision.Capture;
using WindowsAIAssistant.Infrastructure.Vision.Ocr;
using WindowsAIAssistant.Infrastructure.Vision.Processing;
using WindowsAIAssistant.Infrastructure.Vision.Providers;
using WindowsAIAssistant.Infrastructure.Vision.Storage;
using WindowsAIAssistant.Infrastructure.Voice;
using WindowsAIAssistant.Infrastructure.Web;
using WindowsAIAssistant.Infrastructure.Windows;

namespace WindowsAIAssistant.Infrastructure;

public static class DependencyInjection
{
    /// <param name="services">The services to add the implementations to.</param>
    /// <param name="configuration">The configuration to bind options from.</param>
    /// <param name="userSettingsPath">
    /// Where the person's own settings are kept. Defaults to their local application data.
    /// Tests pass a path of their own so they never read or write the settings of whoever is
    /// running them.
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string? userSettingsPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddApplicationConfiguration(configuration);
        services.AddUserSettings(userSettingsPath);
        services.AddWindowsServices();
        services.AddWebSearchProviders();
        services.AddVoiceServices();
        services.AddAIServices();
        services.AddDocumentServices(configuration);
        services.AddEmbeddingServices();
        services.AddKnowledgeServices();
        services.AddVisionServices();

        // After the Application layer's agent registrations, because the stores it registers as
        // fallbacks are the ones this displaces.
        services.AddAgentStore(configuration);

        // File search and settings storage are still the development implementations. The voice
        // layer calls them through the same abstractions, so swapping in the real services later
        // needs no change to any command handler.
        services.AddSingleton<IFileSearchService, MockFileSearchService>();
        services.AddSingleton<ISettingsStorage, InMemorySettingsStorage>();

        return services;
    }

    /// <summary>
    /// Registers document reading.
    /// <para>
    /// This is the only place in the application that knows a document format exists. Every
    /// extractor is registered as an <see cref="IDocumentExtractor"/>, and the factory picks one
    /// by file type, so adding a format is a single registration here and nothing above this
    /// layer changes.
    /// </para>
    /// <para>
    /// The extractors are transient because each one opens the file it is given and holds it
    /// only for the length of one call. Everything above them that is shared is a singleton, so
    /// two documents read at once cannot interfere with each other.
    /// </para>
    /// </summary>
    private static void AddDocumentServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bound from the configuration this method was handed rather than looked up in the
        // container, which is how every other options type here is bound. Looking it up would
        // make document reading the only thing in the application that fails to resolve in a
        // container assembled without configuration registered.
        services.AddSingleton<IValidateOptions<DocumentOptions>, DocumentOptionsValidator>();
        services.AddOptions<DocumentOptions>()
            .Bind(configuration.GetSection(DocumentOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IDocumentTypeDetector, DocumentTypeDetector>();
        services.AddSingleton<IDocumentExtractorFactory, DocumentExtractorFactory>();
        services.AddSingleton<IDocumentReader, DocumentReader>();
        services.AddSingleton<IDocumentChunker, DocumentChunker>();

        services.AddTransient<IDocumentExtractor, PlainTextDocumentExtractor>();
        services.AddTransient<IDocumentExtractor, WordDocumentExtractor>();
        services.AddTransient<IDocumentExtractor, PowerPointDocumentExtractor>();
        services.AddTransient<IDocumentExtractor, ExcelDocumentExtractor>();
        services.AddTransient<IDocumentExtractor, PdfDocumentExtractor>();
    }

    /// <summary>
    /// Registers the AI providers and the single path that reaches them.
    /// <para>
    /// Every provider is registered as <see cref="IAIProvider"/>, and the factory picks between
    /// them by name. That is the whole extension mechanism: a new provider is one registration,
    /// and nothing above Infrastructure changes to use it. The one place the OpenAI SDK is
    /// referenced is this project's own package list, so no abstraction above can name a
    /// provider type even by accident.
    /// </para>
    /// <para>
    /// The provider, the key, and the configuration-backed defaults are all singletons because
    /// they are read per request and hold nothing that has to be isolated between calls. The
    /// client itself is built per request inside the provider, which is what keeps a key from
    /// outliving the call that needed it.
    /// </para>
    /// <para>
    /// The coordinator that turns a request into a provider call is registered by the
    /// Application layer, which owns it: this project supplies the providers it chooses from and
    /// the configuration it chooses by, and never reaches upward to build the policy.
    /// </para>
    /// </summary>
    private static void AddAIServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAIApiKeyProvider, EnvironmentApiKeyProvider>();
        services.AddSingleton<IAIProvider, MockAIProvider>();
        services.AddSingleton<IAIProvider, OpenAIProvider>();
        services.AddSingleton<IAIProviderFactory, AIProviderFactory>();

        // Registered after the Application layer's fallbacks, so the bound configuration is
        // what the coordinator reads. A value saved in Settings therefore takes effect for the
        // very next request.
        services.AddSingleton<IAIRequestDefaults, ConfiguredAIRequestDefaults>();
        services.AddSingleton<IAISystemPromptProvider, ConfiguredSystemPromptProvider>();
    }

    /// <summary>
    /// Registers the embedding providers and the one path that reaches them.
    /// <para>
    /// Every provider is registered as <see cref="IEmbeddingProvider"/> and the factory picks one
    /// by name, so a provider added later is one line. The service that owns batching,
    /// truncation, and the consent check is registered here rather than by the Application layer
    /// because it is this project's job: it holds the option values and the providers.
    /// </para>
    /// </summary>
    private static void AddEmbeddingServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IEmbeddingProvider, MockEmbeddingProvider>();
        services.AddSingleton<IEmbeddingProvider, OpenAIEmbeddingProvider>();
        services.AddSingleton<IEmbeddingProviderFactory, EmbeddingProviderFactory>();
        services.AddSingleton<IEmbeddingService, EmbeddingService>();

        return;
    }

    /// <summary>
    /// Registers the knowledge index.
    /// <para>
    /// The database is a singleton because it owns one file and one schema version, and every
    /// repository reaching it must agree about where that file is. The repositories are
    /// singletons too: each opens a connection per call and holds nothing between calls, so there
    /// is no state to isolate and nothing to lose by sharing one instance.
    /// </para>
    /// <para>
    /// The vector serializer is a singleton for the same reason, and the search service is a
    /// singleton because it holds the options it reads per question rather than per scan.
    /// </para>
    /// <para>
    /// The retrieval and indexing limits the Application layer works to are mapped from
    /// configuration by the composition root rather than here, because this project sits below
    /// Application and cannot see those records. See <c>AddKnowledgeConfiguration</c>.
    /// </para>
    /// </summary>
    private static void AddKnowledgeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<KnowledgeDatabase>();
        services.AddSingleton<IVectorSerializer, Float32VectorSerializer>();

        services.AddSingleton<IKnowledgeBaseRepository, SqliteKnowledgeBaseRepository>();
        services.AddSingleton<IKnowledgeDocumentRepository, SqliteKnowledgeDocumentRepository>();
        services.AddSingleton<IKnowledgeChunkRepository, SqliteKnowledgeChunkRepository>();
        services.AddSingleton<IVectorSearchService, SqliteVectorSearchService>();

        return;
    }

    /// <summary>
    /// Replaces the services that touch the real machine with the development
    /// implementations.
    /// <para>
    /// The production composition root deliberately wires up the real Windows services, so a
    /// test that wants predictable data has to ask for this explicitly rather than receiving
    /// fake values by accident. Call it after <see cref="AddInfrastructure"/>: the last
    /// registration for a service is the one that resolves.
    /// </para>
    /// </summary>
    public static IServiceCollection AddDevelopmentServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IWindowsSystemService, MockWindowsSystemService>();
        services.AddSingleton<IClipboardService, MockClipboardService>();
        services.AddSingleton<IApplicationLauncherService, MockApplicationLauncherService>();

        return services;
    }

    /// <summary>
    /// Registers the platform implementations of the system services.
    /// <para>
    /// Every one of them is a singleton because each wraps a single machine-wide resource such
    /// as the audio endpoint or the Start menu index. Launching, in particular, goes through
    /// the resolver rather than being given a command line, so the only way to start a process
    /// is a name that matched the allow list.
    /// </para>
    /// </summary>
    private static void AddWindowsServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IApplicationResolver, ApplicationResolver>();
        services.AddSingleton<IApplicationLauncherService, WindowsApplicationLauncherService>();
        services.AddSingleton<IWindowsSystemService, WindowsSystemService>();
        services.AddSingleton<IClipboardService, WindowsClipboardService>();
        services.AddSingleton<IKnownFolderService, KnownFolderService>();
        services.AddSingleton<IUriLauncherService, UriLauncherService>();
        services.AddSingleton<IWindowsSettingsService, WindowsSettingsService>();
        services.AddSingleton<IDriveSpaceService, DriveSpaceService>();
        services.AddSingleton<IBatteryService, BatteryService>();
        services.AddSingleton<IVolumeService, VolumeService>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        return;
    }

    private static void AddWebSearchProviders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The voice handler resolves the provider by name, so the collection is the extension
        // point: a new site is a new registration and no handler changes.
        services.AddSingleton<IWebSearchProvider, GoogleSearchProvider>();
        services.AddSingleton<IWebSearchProvider, YouTubeSearchProvider>();
        services.AddSingleton<IWebSearchProvider, GitHubSearchProvider>();
        services.AddSingleton<IWebSearchProvider, StackOverflowSearchProvider>();

        return;
    }

    /// <summary>
    /// Registers the voice pipeline.
    /// <para>
    /// The recognition, synthesis, and control services are singletons because each owns a
    /// device the process may only hold once: two recognizers competing for the microphone, or
    /// two synthesizer sessions, would fight rather than queue. The intent recognizer is also
    /// shared because its rule table is compiled once.
    /// </para>
    /// </summary>
    private static void AddVoiceServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<ISpeechRecognitionService, SpeechRecognitionService>();
        services.AddSingleton<ISpeechSynthesisService, SpeechSynthesisService>();
        services.AddSingleton<IWakeWordService, WakeWordService>();

        return;
    }

    /// <summary>
    /// Registers looking at the screen: capture, local text recognition, image preparation, and
    /// the providers that can answer a question about a picture.
    /// <para>
    /// The capture session manager and the capture service are singletons for the life of the
    /// process. A capture owns graphics resources that Windows will not release until the object
    /// holding them is collected, and creating a second one while a first is alive is how a
    /// machine ends up unable to capture at all after a few dozen screenshots. Everything else is
    /// transient, because each of them is a short operation over bytes somebody is already
    /// holding.
    /// </para>
    /// <para>
    /// The two text engines are both registered and the resolver picks between them, so a machine
    /// with the newer one uses it and a machine without it is not left with nothing. The local
    /// reading is registered last and is the fallback that lets the feature work at all with no
    /// credential and no network.
    /// </para>
    /// <para>
    /// <see cref="IScreenCaptureHost"/> gets a default that reports there is no window, and the
    /// application layer replaces that registration with the real one afterwards. The container
    /// keeps the last registration for a service, so a host that adds its own is in effect rather
    /// than merely competing — and a headless host resolves everything else in this file instead
    /// of failing on a service it was never going to be able to supply.
    /// </para>
    /// </summary>
    private static void AddVisionServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IScreenCaptureHost, UnavailableScreenCaptureHost>();
        services.AddSingleton<GraphicsCaptureSessionManager>();
        services.AddSingleton<IScreenCaptureService>(provider => new ScreenCaptureService(
            provider.GetRequiredService<IScreenCaptureHost>(),
            provider.GetRequiredService<GraphicsCaptureSessionManager>(),
            provider.GetRequiredService<IOptionsMonitor<VisionOptions>>(),
            provider.GetRequiredService<ILogger<ScreenCaptureService>>()));

        services.AddSingleton<IImagePreprocessor, ImagePreprocessor>();
        services.AddSingleton<IScreenshotStore, ScreenshotStore>();

        // An empty engine first, so there is always an implementation to resolve even on a
        // machine with no recogniser at all; it is not available and is never selected.
        services.AddSingleton<IOcrProvider, NoOpOcrProvider>();
        services.AddSingleton<IOcrProvider, WindowsLegacyOcrProvider>();
        services.AddSingleton<IOcrProviderResolver, OcrProviderResolver>();
        services.AddSingleton<IOcrService, OcrService>();

        services.AddSingleton<IVisionProvider, MockVisionProvider>();
        services.AddSingleton<IVisionProvider, OpenAIVisionProvider>();
        services.AddSingleton<IVisionProviderResolver, VisionProviderResolver>();

        // The registry is the one component that stands between a plan and a tool call, so it is
        // registered here rather than in the application layer: it takes the tool instances
        // themselves, and it is the boundary a model's invented tool name cannot cross.
        //
        // TryAdd, so a host that has already chosen its own registry keeps it. A test that
        // supplies a registry of two tools is exercising the executor's real behaviour, and
        // would be quietly testing a different agent if the production registry were forced
        // back over the top of it.
        services.TryAddSingleton<IToolRegistry, ToolRegistry>();

        // Report writers. One registration per format rather than a factory that switches, so
        // that "which formats can this build write" is answered by the registrations themselves
        // and the report tool never needs a branch. Two writers serve the text formats between
        // them; the interface is per-format so the tool can hold a dictionary of them.
        //
        // All singleton: a writer holds no state between reports, and making them shared means
        // the set of available formats is fixed once the container is built.
        services.AddSingleton<IReportWriter>(provider =>
            new TextReportWriter(ReportFormat.Markdown));
        services.AddSingleton<IReportWriter>(provider =>
            new TextReportWriter(ReportFormat.Text));
        services.AddSingleton<IReportWriter, WordReportWriter>();
        services.AddSingleton<IReportWriter, PdfReportWriter>();

        return;
    }

    /// <summary>
    /// Registers the agent's own store and the presentation the workspace reads.
    /// <para>
    /// The Application layer registered the session-only stores as fallbacks with
    /// <c>TryAdd</c>, so these registrations have to displace them rather than join them. They
    /// use <see cref="ServiceCollectionDescriptorExtensions.RemoveAll{TService}"/> and then re-add:
    /// <c>TryAdd</c> here would do nothing at all, because the fallback is already there, and the
    /// result would be an agent that silently kept nothing while the configuration said it was
    /// writing to SQLite.
    /// </para>
    /// <para>
    /// Both stores are chosen from the same option and are the only two implementations of each
    /// interface in the container, so which one is in use can be read off the container rather
    /// than inferred from behaviour. Both keep the same contract and are subject to the same
    /// privacy rules upstream, because those rules live in the memory service and not here.
    /// </para>
    /// </summary>
    private static void AddAgentStore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bound by hand rather than with Bind, so a blank or unrecognised value in the section
        // reads as "not set" instead of throwing from inside the options system.
        services.AddOptions<AgentOptions>()
            .Configure(options => AgentOptionsBinder.Bind(
                options,
                configuration.GetSection(AgentOptions.SectionName)));

        // Registered before the Application layer's default so this one wins. The
        // RemoveAll/AddSingleton dance below is only needed for the stores because those are
        // resolved by interface from several places; the presentation is resolved from one, and
        // taking the last registration is enough.
        services.RemoveAll<IAgentPresentationSettings>();
        services.AddSingleton<IAgentPresentationSettings, ConfiguredAgentPresentationSettings>();

        services.AddSingleton<AgentDatabase>();

        if (AgentOptionsBinder.ReadPersistence(configuration) != AgentPersistenceMode.SessionOnly)
        {
            services.RemoveAll<IAgentMemoryStore>();
            services.RemoveAll<IAgentActivityStore>();

            services.AddSingleton<IAgentMemoryStore, SqliteAgentMemoryStore>();
            services.AddSingleton<IAgentActivityStore, SqliteAgentActivityStore>();
        }

        // In the session-only case the Application layer's fallbacks stand: they are already the
        // in-memory stores, and registering them a second time would make the same implementation
        // resolvable twice.
    }
}
