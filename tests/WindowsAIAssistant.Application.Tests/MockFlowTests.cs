using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WindowsAIAssistant.Application.AI.Commands.SendMessage;
using WindowsAIAssistant.Application.Clipboard.Commands.SetClipboardText;
using WindowsAIAssistant.Application.Clipboard.Queries.GetClipboardText;
using WindowsAIAssistant.Application.Files.Queries.SearchFiles;
using WindowsAIAssistant.Application.System.Queries.GetSystemInformation;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure;
namespace WindowsAIAssistant.Application.Tests;

public sealed class MockFlowTests
{
    [Fact]
    public async Task AiFlow_UsesConfiguredDevelopmentService()
    {
        using var provider = BuildProvider();
        var handler = provider.GetRequiredService<SendMessageHandler>();

        var response = await handler.HandleAsync(new SendMessageCommand("Hello"));

        Assert.True(response.IsSuccessful);
        Assert.Equal(AIProviderType.Local, response.Provider);
        Assert.Equal("mock-model", response.Model);
        Assert.Contains("Hello", response.Content);
    }

    [Fact]
    public async Task FileSearchFlow_ReturnsDevelopmentResults()
    {
        using var provider = BuildProvider();
        var handler = provider.GetRequiredService<SearchFilesHandler>();

        var results = await handler.HandleAsync(new SearchFilesQuery("project"));

        Assert.NotEmpty(results);
        Assert.Contains(results, result => result.Name == "ProjectNotes.docx");
    }

    [Fact]
    public async Task SystemInformationFlow_ReturnsDevelopmentSystemData()
    {
        using var provider = BuildProvider();
        var handler = provider.GetRequiredService<GetSystemInformationHandler>();

        var information = await handler.HandleAsync(new GetSystemInformationQuery());

        Assert.Equal("Development Machine", information.MachineName);
        Assert.Equal("Windows 11", information.OperatingSystem);
    }

    [Fact]
    public async Task ClipboardFlow_RoundTripsTextThroughDevelopmentService()
    {
        using var provider = BuildProvider();
        var setHandler = provider.GetRequiredService<SetClipboardTextHandler>();
        var getHandler = provider.GetRequiredService<GetClipboardTextHandler>();

        var setResult = await setHandler.HandleAsync(new SetClipboardTextCommand("development clipboard"));
        var text = await getHandler.HandleAsync(new GetClipboardTextQuery());

        Assert.True(setResult.IsSuccess);
        Assert.Equal("development clipboard", text);
    }

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        // The production composition root talks to the real machine. These tests want the
        // deterministic development implementations, so they ask for them by name.
        services.AddDevelopmentServices();

        return services.BuildServiceProvider();
    }
}
