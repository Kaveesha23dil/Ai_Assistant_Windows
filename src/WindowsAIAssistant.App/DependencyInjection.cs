using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.App.Services.Navigation;
using WindowsAIAssistant.App.ViewModels;
using WindowsAIAssistant.App.Services;
using WindowsAIAssistant.App.Views;
using WindowsAIAssistant.App.Views.Pages;
using WindowsAIAssistant.Application.Voice;
using WindowsAIAssistant.Application.Documents;
using WindowsAIAssistant.Application.Knowledge;
using WindowsAIAssistant.App.Vision.Ocr;
using WindowsAIAssistant.Core.Abstractions.Navigation;
using WindowsAIAssistant.Core.Abstractions.Vision;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.App;

/// <summary>
/// Registers the presentation layer. ViewModels are resolved by the shell, and pages are
/// registered transiently so each navigation visit gets a fresh page instance.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAppShell(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<UserSettingsService>();

        services.AddNavigation();

        services.AddTransient<HomeViewModel>();
        services.AddTransient<ChatViewModel>();
        services.AddTransient<FilesViewModel>();
        services.AddTransient<DocumentViewModel>();
        services.AddTransient<KnowledgeViewModel>();
        services.AddTransient<AutomationsViewModel>();
    services.AddSingleton<AgentWorkspaceViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddTransient<HomePage>();
        services.AddTransient<ChatPage>();
        services.AddTransient<FilesPage>();
        services.AddTransient<DocumentPage>();
        services.AddTransient<KnowledgePage>();
        services.AddTransient<AutomationsPage>();
    services.AddTransient<AgentWorkspacePage>();
        services.AddTransient<SettingsPage>();

        return services;
    }

    /// <summary>
    /// Registers the one navigation architecture the application has.
    /// <para>
    /// The service is a singleton because it represents a single shared answer to "where is the
    /// application right now": the shell, the sidebar, a quick action, and the voice pipeline
    /// must all be talking about the same page. Registering the same instance under the
    /// toolkit-independent abstraction is deliberate, so the voice layer can ask for a
    /// destination without taking a dependency on a user interface toolkit.
    /// </para>
    /// <para>
    /// The pages themselves stay transient and keep being built by the container when the
    /// service resolves them, so the service routes pages without ever constructing one itself.
    /// </para>
    /// </summary>
    public static IServiceCollection AddNavigation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The registry is built through a factory that names the service type. Handing the
        // method on its own would register the delegate as the service instead, and the
        // container would then be unable to supply the table the service is built from.
        services.AddSingleton(_ => NavigationRouteRegistry.CreateDefault());
        services.AddSingleton<NavigationService>();

        // The three registrations below resolve the very same instance; none of them may build
        // a second one, or the sidebar and the voice layer would disagree about the current page.
        services.AddSingleton<INavigationService>(provider => provider.GetRequiredService<NavigationService>());
        services.AddSingleton<IApplicationNavigator>(provider => provider.GetRequiredService<NavigationService>());

        return services;
    }

    /// <summary>
    /// Translates the bound <c>Voice</c> configuration into the Application layer's policy.
    /// <para>
    /// The Application project cannot do this itself: it describes what the assistant may do but
    /// deliberately has no dependency on Infrastructure, where <c>VoiceOptions</c> lives. This
    /// is the one place in the solution that sees both, so the mapping is written once here
    /// rather than repeated by every host.
    /// </para>
    /// <para>
    /// The policy is a singleton, so it is read once when the pipeline is first built. A
    /// command already in flight therefore keeps the behaviour it started with rather than
    /// changing halfway through because a setting was edited. Consent is a different matter
    /// and is re-read per operation by the permission service, so revoking microphone access
    /// takes effect on the very next request. When the person saves a change, the policy is
    /// handed a new set of values as a whole, which is the one moment a running pipeline is
    /// allowed to pick up new decisions.
    /// </para>
    /// </summary>
    public static IServiceCollection AddVoiceConfiguration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp =>
        {
            var policy = new VoiceIntentPolicy();
            policy.Update(CreatePolicyValues(sp.GetRequiredService<IOptions<VoiceOptions>>().Value));

            return policy;
        });

        return services;
    }

    /// <summary>
    /// Registers the two pieces of the screen feature that only this project can supply.
    /// <para>
    /// The capture host is the window and the thread the Windows capture picker belongs to. The
    /// newer text recogniser is here because the Windows AI text APIs arrive with the application
    /// SDK that this project already references, and the feature deliberately does not add that
    /// dependency to the layer underneath, which has no use for it.
    /// </para>
    /// <para>
    /// Called after <c>AddInfrastructure</c>, so both text engines are registered by the time
    /// the resolver is built. It does not matter that Infrastructure registered two and this adds
    /// a third: the resolver keys them by engine rather than by position, so a machine with the
    /// newer recogniser uses it and a machine without it is not left with nothing.
    /// </para>
    /// </summary>
    public static IServiceCollection AddScreenVisionHosting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<WindowCaptureHost>();
        services.AddSingleton<IScreenCaptureHost>(sp => sp.GetRequiredService<WindowCaptureHost>());
        services.AddSingleton<IOcrProvider, WindowsAiOcrProvider>();

        return services;
    }

    /// <summary>
    /// Maps the bound document configuration onto the Application layer's limits. Written here
    /// for the same reason as the voice policy above: this is the only place in the solution
    /// that sees both the options type and the value the application works to, and the
    /// Application project deliberately cannot build one from the other.
    /// </summary>
    public static DocumentProcessingLimits CreateDocumentLimits(DocumentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new DocumentProcessingLimits
        {
            MaximumCharactersPerRequest = options.MaximumCharactersPerRequest,
            MaximumChunksPerRequest = options.MaximumChunksPerRequest,
            MaximumCombinedSummaries = options.MaximumCombinedSummaries,
            MaximumConcurrentChunkSummaries = options.MaximumConcurrentChunkSummaries,
        };
    }

    /// <summary>
    /// Registers the document limits from configuration, replacing the Application layer's
    /// defaults. Read once, so a document being read keeps the limits it started with.
    /// </summary>
    public static IServiceCollection AddDocumentConfiguration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(provider =>
            CreateDocumentLimits(provider.GetRequiredService<IOptions<DocumentOptions>>().Value));

        return services;
    }

    /// <summary>
    /// Maps the bound knowledge configuration onto the Application layer's own values, for the
    /// same reason as the document limits above: this is the only place that sees both the
    /// options type and the record the application works to, and the Application project cannot
    /// reference Infrastructure to build one from the other.
    /// </summary>
    public static KnowledgeProcessingLimits CreateKnowledgeLimits(KnowledgeBaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new KnowledgeProcessingLimits
        {
            Enabled = options.Enabled,
            DatabaseFileName = options.DatabaseFileName,
            MaximumDocuments = options.MaximumDocuments,
            MaximumChunksPerDocument = options.MaximumChunksPerDocument,
            DefaultRetrievalCount = options.DefaultRetrievalCount,
            MaximumRetrievalCount = options.MaximumRetrievalCount,
            MaximumScannedChunks = options.MaximumScannedChunks,
        };
    }

    /// <summary>
    /// Maps the bound retrieval configuration onto the Application layer's ranking policy.
    /// <para>
    /// The candidate multiplier is not configuration. It is a property of the ranking — how far
    /// ahead of the quota the store is asked to look, so the threshold and the redundancy pass
    /// have something to remove — and a person with a reason to disagree would be disagreeing
    /// about their own relevance judgement rather than about their hardware. The default is kept.
    /// </para>
    /// </summary>
    public static RagRetrievalPolicy CreateRagPolicy(RagOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new RagRetrievalPolicy
        {
            Enabled = options.Enabled,
            TopK = options.TopK,
            MaximumContextCharacters = options.MaximumContextCharacters,
            MinimumSimilarity = options.MinimumSimilarity,
            UseHybridSearch = options.UseHybridSearch,
            VectorWeight = options.VectorWeight,
            LexicalWeight = options.LexicalWeight,
            SectionTitleWeight = options.SectionTitleWeight,
            FileNameWeight = options.FileNameWeight,
            MaximumChunksPerDocument = options.MaximumChunksPerDocument,
            MaximumRedundancyRatio = options.MaximumRedundancyRatio,
        };
    }

    /// <summary>
    /// Registers the knowledge and retrieval values from configuration, replacing the defaults
    /// the Application layer registered. Read once, so a question being assembled keeps the
    /// ranking it started with and a document being indexed keeps the limits it started with.
    /// </summary>
    public static IServiceCollection AddKnowledgeConfiguration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(provider =>
            CreateKnowledgeLimits(provider.GetRequiredService<IOptions<KnowledgeBaseOptions>>().Value));

        services.AddSingleton(provider =>
            CreateRagPolicy(provider.GetRequiredService<IOptions<RagOptions>>().Value));

        return services;
    }

    /// <summary>
    /// Maps the bound voice configuration onto the Application layer's policy values. Written
    /// once here because this is the only place in the solution that sees both types, and
    /// reused when a save has to be applied to a running session.
    /// </summary>
    public static VoiceIntentPolicyValues CreatePolicyValues(VoiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new VoiceIntentPolicyValues
        {
            Enabled = options.Enabled,
            SpeakResponses = options.SpeakResponses,
            EnableContinuousListening = options.ContinuousListening,
            Language = options.Language,
            MinimumCommandConfidence = options.MinimumCommandConfidence,
            ConfirmLowConfidenceCommands = options.ConfirmLowConfidenceCommands,
            MaximumSpokenResponseLength = options.MaximumSpokenResponseLength,
        };
    }
}
