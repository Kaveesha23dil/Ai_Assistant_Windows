using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.AI.Commands.SendMessage;
using WindowsAIAssistant.Application.AI.Queries.GetConversation;
using WindowsAIAssistant.Application.AI.Services;
using WindowsAIAssistant.Application.Clipboard.Commands.SetClipboardText;
using WindowsAIAssistant.Application.Clipboard.Queries.GetClipboardText;
using WindowsAIAssistant.Application.Common.Errors;
using WindowsAIAssistant.Application.Documents;
using WindowsAIAssistant.Application.Documents.Queries.AskDocumentQuestion;
using WindowsAIAssistant.Application.Documents.Queries.StreamDocumentAnswer;
using WindowsAIAssistant.Application.Documents.Queries.SummarizeDocument;
using WindowsAIAssistant.Application.Files.Queries.SearchFiles;
using WindowsAIAssistant.Application.Knowledge;
using WindowsAIAssistant.Application.Knowledge.Commands.ManageKnowledgeBase;
using WindowsAIAssistant.Application.Knowledge.Commands.ManageKnowledgeDocuments;
using WindowsAIAssistant.Application.Knowledge.Queries.AskKnowledgeQuestion;
using WindowsAIAssistant.Application.Knowledge.Queries.GetKnowledgeOverview;
using WindowsAIAssistant.Application.Navigation;
using WindowsAIAssistant.Application.Settings.Commands.SaveUserSettings;
using WindowsAIAssistant.Application.Settings.Commands.UpdateSetting;
using WindowsAIAssistant.Application.Settings.Queries.GetSetting;
using WindowsAIAssistant.Application.System.Commands.LaunchApplication;
using WindowsAIAssistant.Application.System.Queries.GetSystemInformation;
using WindowsAIAssistant.Application.Voice.Commands.AiQuestion;
using WindowsAIAssistant.Application.Voice.Commands.AssistantControl;
using WindowsAIAssistant.Application.Voice.Commands.Clipboard;
using WindowsAIAssistant.Application.Voice.Commands.Document;
using WindowsAIAssistant.Application.Voice.Commands.FileSearch;
using WindowsAIAssistant.Application.Voice.Commands.Knowledge;
using WindowsAIAssistant.Application.Voice.Commands.Navigation;
using WindowsAIAssistant.Application.Voice.Commands.OpenApplication;
using WindowsAIAssistant.Application.Voice.Commands.OpenFolder;
using WindowsAIAssistant.Application.Voice.Commands.Screen;
using WindowsAIAssistant.Application.Voice.Commands.Screenshot;
using WindowsAIAssistant.Application.Voice.Commands.SystemInformation;
using WindowsAIAssistant.Application.Voice.Commands.Volume;
using WindowsAIAssistant.Application.Voice.Commands.WebSearch;
using WindowsAIAssistant.Application.Voice;
using WindowsAIAssistant.Application.Voice.Services;
using WindowsAIAssistant.Application.Vision;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Abstractions.Knowledge;
using WindowsAIAssistant.Core.Abstractions.Navigation;
using WindowsAIAssistant.Core.Abstractions.Vision;
using WindowsAIAssistant.Core.Abstractions.Voice;

namespace WindowsAIAssistant.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();
        services.AddSingleton<IErrorHandler, ErrorHandler>();
        services.AddSingleton<IConversationService, ConversationService>();
        services.AddAIService();
        services.AddTransient<SendMessageHandler>();
        services.AddTransient<StreamMessageHandler>();
        services.AddTransient<GetConversationHandler>();
        services.AddTransient<SearchFilesHandler>();
        services.AddTransient<GetSystemInformationHandler>();
        services.AddTransient<LaunchApplicationHandler>();
        services.AddTransient<GetClipboardTextHandler>();
        services.AddTransient<SetClipboardTextHandler>();
        services.AddTransient(typeof(GetSettingHandler<>));
        services.AddTransient(typeof(UpdateSettingHandler<>));
        services.AddTransient<SaveUserSettingsHandler>();

        services.AddDocuments();

        services.AddKnowledge();

        services.AddVision();

        services.AddVoiceAssistant();

        return services;
    }

    /// <summary>
    /// Registers the screen-vision features.
    /// <para>
    /// The analysis service is a singleton for the same reason the knowledge indexing service is:
    /// it owns the one slot where a raw frame lives while a request is in flight, and that slot is
    /// only meaningful if every caller shares it. A second instance would mean a second frame
    /// buffer, which is precisely the accumulation this feature is built to avoid.
    /// </para>
    /// <para>
    /// The consent policy is a singleton so that there is one place in the application where the
    /// answer to "may a screenshot be sent" is decided. The capture, OCR, preprocessing, and
    /// provider implementations are registered by Infrastructure with <c>TryAdd</c> semantics, so
    /// the Application layer stays constructible with no platform behind it and every test can
    /// supply a fake that returns three different frames in a row.
    /// </para>
    /// </summary>
    public static IServiceCollection AddVision(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IScreenContextService, ScreenContextService>();
        services.TryAddSingleton<ScreenConsentPolicy>();
        services.TryAddSingleton<IScreenAnalysisService, ScreenAnalysisService>();

        services.AddTransient<IAssistantActionExecutor, ScreenVoiceHandler>();

        return services;
    }

    /// <summary>
    /// Registers the knowledge features.
    /// <para>
    /// The two records are registered here with their own defaults so that the layer stays
    /// constructible without a host, exactly as the document limits and the voice policy are.
    /// A composition root that has Infrastructure in front of it maps its bound configuration over
    /// them afterwards, and its registrations win because it comes second — the same precedence
    /// the configured AI request defaults rely on.
    /// </para>
    /// <para>
    /// The ranker and the retriever are singletons because they hold nothing between questions,
    /// and the context builder is stateless. The indexing service is a singleton too, but for a
    /// different reason: it owns the in-flight set that stops the same file being indexed twice at
    /// once, and that set is only useful if every caller shares one of them.
    /// </para>
    /// </summary>
    public static IServiceCollection AddKnowledge(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(new KnowledgeProcessingLimits());
        services.TryAddSingleton(new RagRetrievalPolicy());
        services.TryAddSingleton<IHybridSearchRanker, HybridSearchRanker>();
        services.TryAddSingleton<IRagRetriever, RagRetriever>();
        services.TryAddSingleton<IRagContextBuilder, RagContextBuilder>();
        services.TryAddSingleton<IRagService, RagService>();
        services.TryAddSingleton<IKnowledgeIndexingService, KnowledgeIndexingService>();

        // Transient, like every other handler here: they hold no state between requests, and
        // making them singletons would put a half-read list of documents into a shared object
        // that the voice path and the page are both looking at.
        services.AddTransient<GetKnowledgeOverviewHandler>();
        services.AddTransient<ManageKnowledgeBaseHandler>();
        services.AddTransient<ManageKnowledgeDocumentsHandler>();
        services.AddTransient<AskKnowledgeQuestionHandler>();

        return services;
    }

    /// <summary>
    /// Registers the document features.
    /// <para>
    /// The analysis service is registered here because it owns the policy: which passages of a
    /// document are worth sending, how a long one is handled, and what an answer is allowed to
    /// claim. Everything above it — the handlers, the voice path, the view models — reaches
    /// documents only through it.
    /// </para>
    /// <para>
    /// The ranker is a singleton because it holds nothing between calls and is asked to rank
    /// once per keystroke of a question. The limits are a singleton value rather than an options
    /// monitor so that they are fixed for the length of a request.
    /// </para>
    /// </summary>
    public static IServiceCollection AddDocuments(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<DocumentProcessingLimits>();
        services.TryAddSingleton<DocumentCloudConsentPolicy>();
        services.TryAddSingleton<IDocumentChunkRanker, DocumentChunkRanker>();
        services.TryAddSingleton<IDocumentAnalysisService, DocumentAnalysisService>();

    services.AddTransient<SummarizeDocumentHandler>();
    services.AddTransient<AskDocumentQuestionHandler>();
    services.AddTransient<StreamDocumentAnswerHandler>();

        return services;
    }

    /// <summary>
    /// Registers the AI path that every caller shares.
    /// <para>
    /// The coordinator is a singleton because it holds no per-request state: one registration
    /// means chat, voice, and every other caller demonstrably reach the same provider selection,
    /// the same consent check, and the same error translation. Two of them would be a bug that
    /// nothing would report.
    /// </para>
    /// <para>
    /// The prompt and the request defaults are registered as fallbacks rather than required
    /// services. A host that binds configuration replaces both, and this keeps the Application
    /// layer constructible on its own, which is what lets the voice pipeline and the handlers
    /// be built and tested without an Infrastructure project behind them.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAIService(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAIRequestDefaults>(new StaticAIRequestDefaults());
        services.TryAddSingleton<IAISystemPromptProvider>(
            new StaticSystemPromptProvider(DefaultSystemPrompt.Text));
        services.TryAddSingleton<AIConversationContextBuilder>();
        services.TryAddSingleton<IAIService, AIService>();

        return services;
    }

    /// <summary>
    /// Registers the voice pipeline.
    /// <para>
    /// The session, the assistant, and the control surface are singletons because the
    /// assistant holds the state the user interface binds to and the session is the shared
    /// runtime state the handlers read. Each command handler is transient: it holds no state
    /// of its own, and building it per command keeps the routing table cheap to extend.
    /// </para>
    /// <para>
    /// <c>VoiceIntentPolicy</c> is registered here with its own defaults so the pipeline can
    /// be constructed without a host, but a host that reads configuration replaces it. The
    /// Application project cannot build the policy from <c>VoiceOptions</c> because it
    /// deliberately has no dependency on Infrastructure, so a composition root that has both
    /// registers its own instance to take precedence.
    /// </para>
    /// </summary>
    public static IServiceCollection AddVoiceAssistant(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(new VoiceIntentPolicy());

        // The pipeline can be built without a window, and a command that needs navigation has
        // to be routable there or the container would not resolve. The placeholder reports that
        // the destination is unavailable; a host with a shell registers its own navigator after
        // this and the placeholder is never consulted.
        services.TryAddSingleton<IApplicationNavigator, NullApplicationNavigator>();

        services.AddSingleton<AssistantSession>();
        services.AddSingleton<IVoiceCommandHistory, VoiceCommandHistory>();
        services.AddSingleton<IIntentRecognizer, IntentRecognizer>();
        services.AddSingleton<IAssistantActionRegistry, AssistantActionRegistry>();
        services.AddSingleton<ICommandRouter, CommandRouter>();
        services.AddSingleton<IVoiceAssistantControl, VoiceAssistantControl>();
        services.AddSingleton<IVoiceAssistantService, VoiceAssistantService>();

        services.AddTransient<IAssistantActionExecutor, OpenApplicationVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, OpenFolderVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, OpenSettingsVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, NavigateVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, WebSearchVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, FileSearchVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, DocumentVoiceHandler>();

        // Before the AI question handler on purpose. The registry resolves by intent, so order
        // here does not decide which one runs, but the two are registered adjacently because
        // they are the two answers a question can get, and reading them together is how a change
        // to one is checked against the other.
        services.AddTransient<IAssistantActionExecutor, KnowledgeVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, SystemInformationVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, BatteryVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, TimeAndDateVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, VolumeVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, ClipboardVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, ScreenshotVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, AssistantControlVoiceHandler>();
        services.AddTransient<IAssistantActionExecutor, AiQuestionVoiceHandler>();

        return services;
    }
}
