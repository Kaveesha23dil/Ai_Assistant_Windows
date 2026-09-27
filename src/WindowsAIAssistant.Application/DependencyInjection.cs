using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.AI.Commands.SendMessage;
using WindowsAIAssistant.Application.AI.Queries.GetConversation;
using WindowsAIAssistant.Application.AI.Services;
using WindowsAIAssistant.Application.Clipboard.Commands.SetClipboardText;
using WindowsAIAssistant.Application.Clipboard.Queries.GetClipboardText;
using WindowsAIAssistant.Application.Common.Errors;
using WindowsAIAssistant.Application.Files.Queries.SearchFiles;
using WindowsAIAssistant.Application.Navigation;
using WindowsAIAssistant.Application.Settings.Commands.SaveUserSettings;
using WindowsAIAssistant.Application.Settings.Commands.UpdateSetting;
using WindowsAIAssistant.Application.Settings.Queries.GetSetting;
using WindowsAIAssistant.Application.System.Commands.LaunchApplication;
using WindowsAIAssistant.Application.System.Queries.GetSystemInformation;
using WindowsAIAssistant.Application.Voice.Commands.AiQuestion;
using WindowsAIAssistant.Application.Voice.Commands.AssistantControl;
using WindowsAIAssistant.Application.Voice.Commands.Clipboard;
using WindowsAIAssistant.Application.Voice.Commands.FileSearch;
using WindowsAIAssistant.Application.Voice.Commands.Navigation;
using WindowsAIAssistant.Application.Voice.Commands.OpenApplication;
using WindowsAIAssistant.Application.Voice.Commands.OpenFolder;
using WindowsAIAssistant.Application.Voice.Commands.Screenshot;
using WindowsAIAssistant.Application.Voice.Commands.SystemInformation;
using WindowsAIAssistant.Application.Voice.Commands.Volume;
using WindowsAIAssistant.Application.Voice.Commands.WebSearch;
using WindowsAIAssistant.Application.Voice;
using WindowsAIAssistant.Application.Voice.Services;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Navigation;
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

        services.AddVoiceAssistant();

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
