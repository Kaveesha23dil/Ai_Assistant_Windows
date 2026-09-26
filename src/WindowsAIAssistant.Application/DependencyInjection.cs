using Microsoft.Extensions.DependencyInjection;
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

        return services;
    }
}
