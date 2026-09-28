using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
using WindowsAIAssistant.Application.Settings.Commands.UpdateSetting;
using WindowsAIAssistant.Application.Settings.Queries.GetSetting;
using WindowsAIAssistant.Application.System.Commands.LaunchApplication;
using WindowsAIAssistant.Application.System.Queries.GetSystemInformation;
using WindowsAIAssistant.Application.Voice.Services;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Abstractions.Files;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Storage;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;
using WindowsAIAssistant.Infrastructure;
using WindowsAIAssistant.Infrastructure.Development;
using WindowsAIAssistant.Infrastructure.Voice;
using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Infrastructure.Windows;

namespace WindowsAIAssistant.Application.Tests;

public sealed class DIResolutionTests
{
    [Fact]
    public void AddApplicationAndInfrastructure_RegistersRequiredServices()
    {
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration, AbsentSettingsFile.FilePath);

        using var provider = services.BuildServiceProvider();

        // The production composition root is wired to the real machine, not to the
        // development stand-ins. Asserting that here is what stops a test from passing
        // against fake data while the application would have used the real thing.
        Assert.IsType<WindowsSystemService>(provider.GetRequiredService<IWindowsSystemService>());
        Assert.IsType<WindowsClipboardService>(provider.GetRequiredService<IClipboardService>());
        Assert.IsType<WindowsApplicationLauncherService>(provider.GetRequiredService<IApplicationLauncherService>());

    // AI now resolves to the coordinator, which is what every caller should be talking to. The
    // offline provider is one of the candidates behind it rather than the service itself, and
    // file search is still the development one: no real provider has been built yet.
    Assert.IsType<AIService>(provider.GetRequiredService<IAIService>());
    Assert.Contains(provider.GetServices<IAIProvider>(), candidate => candidate.ProviderType == AIProviderType.Mock);
    Assert.IsType<MockFileSearchService>(provider.GetRequiredService<IFileSearchService>());
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
    public void AddApplicationAndInfrastructure_RegistersTheDocumentEntryPoints()
    {
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration, AbsentSettingsFile.FilePath);

        using var provider = services.BuildServiceProvider();

        // All three ways of asking a document something resolve from the same service. The
        // streaming one is a separate entry point rather than an optional extra, and a missing
        // registration for it would only show up as an empty page when a person asked a
        // question, which is exactly the case that looks like the application hanging.
        Assert.NotNull(provider.GetRequiredService<SummarizeDocumentHandler>());
        Assert.NotNull(provider.GetRequiredService<AskDocumentQuestionHandler>());
        Assert.NotNull(provider.GetRequiredService<StreamDocumentAnswerHandler>());

        // And the service itself is one instance behind all of them, so the limits and the
        // consent decision cannot differ between the streaming and whole-answer paths.
        Assert.Same(
            provider.GetRequiredService<IDocumentAnalysisService>(),
            provider.GetRequiredService<IDocumentAnalysisService>());
        Assert.NotNull(provider.GetRequiredService<DocumentProcessingLimits>());
        Assert.NotNull(provider.GetRequiredService<DocumentCloudConsentPolicy>());
    }

    [Fact]
    public void AddApplicationAndInfrastructure_RegistersTheVoicePipeline()
    {
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration, AbsentSettingsFile.FilePath);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<SpeechRecognitionService>(provider.GetRequiredService<ISpeechRecognitionService>());
        Assert.IsType<SpeechSynthesisService>(provider.GetRequiredService<ISpeechSynthesisService>());
        Assert.IsType<PermissionService>(provider.GetRequiredService<IPermissionService>());
        Assert.IsType<WakeWordService>(provider.GetRequiredService<IWakeWordService>());

        Assert.NotNull(provider.GetRequiredService<IVoiceAssistantService>());
        Assert.NotNull(provider.GetRequiredService<IVoiceAssistantControl>());
        Assert.NotNull(provider.GetRequiredService<IIntentRecognizer>());
        Assert.NotNull(provider.GetRequiredService<ICommandRouter>());
        Assert.NotNull(provider.GetRequiredService<AssistantSession>());
        Assert.NotNull(provider.GetRequiredService<IAssistantActionRegistry>());
    }

    [Fact]
    public void VoiceServices_AreSingletonsBecauseTheyOwnADevice()
    {
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration, AbsentSettingsFile.FilePath);

        using var provider = services.BuildServiceProvider();

        // Two recognizers or two synthesizers would fight over the same device rather than
        // queue, so the pipeline is shared.
        Assert.Same(
            provider.GetRequiredService<ISpeechRecognitionService>(),
            provider.GetRequiredService<ISpeechRecognitionService>());
        Assert.Same(
            provider.GetRequiredService<ISpeechSynthesisService>(),
            provider.GetRequiredService<ISpeechSynthesisService>());
        Assert.Same(
            provider.GetRequiredService<IVoiceAssistantService>(),
            provider.GetRequiredService<IVoiceAssistantService>());
    }

    /// <summary>
    /// The two intents that deliberately have no executor, and why.
    /// </summary>
    private static readonly AssistantIntent[] IntentsWithoutExecutors =
    [
        // Answered by the assistant itself. Confirmation has to re-route the command that was
        // held back, which happens before the router is consulted, so routing "confirm" to a
        // handler would recurse.
        AssistantIntent.ConfirmCommand,

        // Closing an application means terminating a process the user owns. The assistant
        // launches applications on request but keeps no handle on them, so there is no safe
        // target to close: implementing this would mean killing a process chosen by name, which
        // is the behaviour the safety rules forbid. The router answers "unavailable" instead.
        AssistantIntent.CloseApplication,
    ];

    [Fact]
    public void EveryVoiceIntentHasAnExecutorOrIsHandledDeliberately()
    {
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration, AbsentSettingsFile.FilePath);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAssistantActionRegistry>();

        // An intent with no executor and no reason would be a command the assistant confidently
        // claims to understand and then does nothing about.
        var unhandled = Enum.GetValues<AssistantIntent>()
            .Where(intent => intent != AssistantIntent.Unknown)
            .Where(intent => !registry.RegisteredIntents.Contains(intent))
            .Where(intent => !IntentsWithoutExecutors.Contains(intent))
            .ToArray();

        Assert.Empty(unhandled);

        // The exemptions are a short, reviewable list rather than an open-ended gap, and the
        // exemptions really are absent from the registry.
        Assert.All(
            IntentsWithoutExecutors,
            intent => Assert.DoesNotContain(intent, registry.RegisteredIntents));
    }

    [Fact]
    public async Task AnIntentWithNoExecutorIsRefusedRatherThanIgnored()
    {
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration, AbsentSettingsFile.FilePath);

        using var provider = services.BuildServiceProvider();
        var router = provider.GetRequiredService<ICommandRouter>();

        var result = await router.RouteAsync(
            VoiceCommand.Create(
                "close notepad",
                AssistantIntent.CloseApplication,
                safetyLevel: ActionSafetyLevel.ConfirmationRequired,
                requiresConfirmation: true));

        // The user gets a spoken explanation, not a silent no-op and not a killed process.
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.ResponseText));
        Assert.DoesNotContain(AssistantIntent.CloseApplication, router.SupportedIntents);
    }

    [Fact]
    public void AddDevelopmentServices_ReplacesTheRealMachineServices()
    {
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration, AbsentSettingsFile.FilePath);
        services.AddDevelopmentServices();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<MockWindowsSystemService>(provider.GetRequiredService<IWindowsSystemService>());
        Assert.IsType<MockClipboardService>(provider.GetRequiredService<IClipboardService>());
        Assert.IsType<MockApplicationLauncherService>(provider.GetRequiredService<IApplicationLauncherService>());

        // The voice pipeline is not replaced: the development services stand in for the
        // machine, not for the assistant.
        Assert.IsType<SpeechRecognitionService>(provider.GetRequiredService<ISpeechRecognitionService>());
    }

    [Fact]
    public void AddApplicationAndInfrastructure_UsesSingletonLifetimesForStateServices()
    {
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration, AbsentSettingsFile.FilePath);

        using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IAIService>(), provider.GetRequiredService<IAIService>());
        Assert.Same(provider.GetRequiredService<IClipboardService>(), provider.GetRequiredService<IClipboardService>());
        Assert.Same(provider.GetRequiredService<ISettingsStorage>(), provider.GetRequiredService<ISettingsStorage>());
    }
}
