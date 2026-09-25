using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using WindowsAIAssistant.Application;
using WindowsAIAssistant.Infrastructure;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly IHost _host;
    private readonly ILogger<App> _logger;
    private Window? _window;
    private bool _hostStopped;

    public App()
    {
        InitializeComponent();
        _host = CreateHost();
        _logger = _host.Services.GetRequiredService<ILogger<App>>();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        try
        {
            await _host.StartAsync();
            var environment = _host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName;
            var aiOptions = _host.Services.GetRequiredService<IOptions<AIOptions>>().Value;
            _logger.LogInformation(
                "Windows AI Assistant started in {Environment} environment. AI provider configured as {Provider}.",
                environment,
                aiOptions.Provider);

            _window = _host.Services.GetRequiredService<MainWindow>();
            _window.Closed += (_, _) => _ = ShutdownHostAsync();
            _window.Activate();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Windows AI Assistant failed to start.");
            await ShutdownHostAsync();
            throw;
        }
    }

    private static IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.SetMinimumLevel(
                    context.HostingEnvironment.IsDevelopment()
                        ? LogLevel.Debug
                        : LogLevel.Information);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddApplication();
                services.AddInfrastructure(context.Configuration);
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    private async Task ShutdownHostAsync()
    {
        if (_hostStopped)
        {
            return;
        }

        _hostStopped = true;

        try
        {
            await _host.StopAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Windows AI Assistant host shutdown failed.");
        }
        finally
        {
            _host.Dispose();
        }
    }
}
