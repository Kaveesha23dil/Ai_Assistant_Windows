using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Abstractions.Storage;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Infrastructure;
using WindowsAIAssistant.Infrastructure.Development;

namespace WindowsAIAssistant.Application.Tests;

public sealed class DIResolutionTests
{
    [Fact]
    public void AddApplicationAndInfrastructure_RegistersRequiredServices()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<MockAIService>(provider.GetRequiredService<IAIService>());
        Assert.IsType<MockWindowsSystemService>(provider.GetRequiredService<IWindowsSystemService>());
        Assert.IsType<MockApplicationLauncherService>(provider.GetRequiredService<IApplicationLauncherService>());
        Assert.IsType<MockFileSearchService>(provider.GetRequiredService<IFileSearchService>());
        Assert.IsType<MockClipboardService>(provider.GetRequiredService<IClipboardService>());
        Assert.IsType<InMemorySettingsStorage>(provider.GetRequiredService<ISettingsStorage>());
        Assert.NotNull(provider.GetRequiredService<IConversationService>());
        Assert.NotNull(provider.GetRequiredService<IErrorHandler>());
        Assert.NotNull(provider.GetRequiredService<SendMessageHandler>());
        Assert.NotNull(provider.GetRequiredService<GetConversationHandler>());
        Assert.NotNull(provider.GetRequiredService<SearchFilesHandler>());
        Assert.NotNull(provider.GetRequiredService<GetSystemInformationHandler>());
        Assert.NotNull(provider.GetRequiredService<LaunchApplicationHandler>());
        Assert.NotNull(provider.GetRequiredService<GetClipboardTextHandler>());
        Assert.NotNull(provider.GetRequiredService<SetClipboardTextHandler>());
        Assert.NotNull(provider.GetRequiredService<GetSettingHandler<string>>());
        Assert.NotNull(provider.GetRequiredService<UpdateSettingHandler<string>>());
    }

    [Fact]
    public void AddApplicationAndInfrastructure_UsesSingletonLifetimesForStateServices()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IAIService>(), provider.GetRequiredService<IAIService>());
        Assert.Same(provider.GetRequiredService<IClipboardService>(), provider.GetRequiredService<IClipboardService>());
        Assert.Same(provider.GetRequiredService<ISettingsStorage>(), provider.GetRequiredService<ISettingsStorage>());
    }
}
