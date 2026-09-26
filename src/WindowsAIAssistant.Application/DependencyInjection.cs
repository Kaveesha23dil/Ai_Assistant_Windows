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
using WindowsAIAssistant.Application.Settings.Commands.UpdateSetting;
using WindowsAIAssistant.Application.Settings.Queries.GetSetting;
using WindowsAIAssistant.Application.System.Commands.LaunchApplication;
using WindowsAIAssistant.Application.System.Queries.GetSystemInformation;
using WindowsAIAssistant.Application.Voice.Commands.AiQuestion;
using WindowsAIAssistant.Application.Voice.Commands.AssistantControl;
using WindowsAIAssistant.Application.Voice.Commands.Clipboard;
using WindowsAIAssistant.Application.Voice.Commands.FileSearch;
using WindowsAIAssistant.Application.Voice.Commands.OpenApplication;
using WindowsAIAssistant.Application.Voice.Commands.OpenFolder;
using WindowsAIAssistant.Application.Voice.Commands.Screenshot;
using WindowsAIAssistant.Application.Voice.Commands.SystemInformation;
using WindowsAIAssistant.Application.Voice.Commands.Volume;
using WindowsAIAssistant.Application.Voice.Commands.WebSearch;
using WindowsAIAssistant.Application.Voice;
using WindowsAIAssistant.Application.Voice.Services;
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
        services.AddTransient<SendMessageHandler>();
        services.AddTransient<GetConversationHandler>();
        services.AddTransient<SearchFilesHandler>();
        services.AddTransient<GetSystemInformationHandler>();
        services.AddTransient<LaunchApplicationHandler>();
        services.AddTransient<GetClipboardTextHandler>();
        services.AddTransient<SetClipboardTextHandler>();
        services.AddTransient(typeof(GetSettingHandler<>));
        services.AddTransient(typeof(UpdateSettingHandler<>));

        services.AddVoiceAssistant();

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
