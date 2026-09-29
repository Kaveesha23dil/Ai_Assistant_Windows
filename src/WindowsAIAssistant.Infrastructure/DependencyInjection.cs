using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Abstractions.Embeddings;
using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Storage;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Time;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Abstractions.Web;
using WindowsAIAssistant.Infrastructure.AI;
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
}
