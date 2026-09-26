using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using WindowsAIAssistant.App.Views;
using WindowsAIAssistant.Application;
using WindowsAIAssistant.Application.Common.Errors;
using WindowsAIAssistant.Infrastructure;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using WindowsAIAssistant.Infrastructure.Logging;

namespace WindowsAIAssistant.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly IHost _host;
    private readonly ILogger<App> _logger;
    private readonly IErrorHandler _errorHandler;
    private Window? _window;
    private bool _hostStopped;

    public App()
    {
        InitializeComponent();
        _host = CreateHost();
        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        _errorHandler = _host.Services.GetRequiredService<IErrorHandler>();

        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
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
            _logger.LogInformation("Windows AI Assistant starting.");

            var environment = _host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName;
            _logger.LogInformation("Environment detected: {Environment}.", environment);

            var aiOptions = _host.Services.GetRequiredService<IOptions<AIOptions>>().Value;
            _logger.LogInformation("AI provider type: {Provider}.", aiOptions.Provider);

            _logger.LogInformation("Application services configured.");

            await _host.StartAsync();
            _logger.LogInformation("Application started successfully.");

            _window = _host.Services.GetRequiredService<MainWindow>();
            _window.Closed += (_, _) => _ = ShutdownHostAsync();
            _window.Activate();
        }
        catch (OptionsValidationException exception)
        {
            _logger.LogError(
                "Configuration validation failed for {Section}.",
                exception.OptionsType?.Name ?? "Configuration");
            await ShutdownHostAsync();
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Application failed to start.");
            await ShutdownHostAsync();
            throw;
        }
    }

    private static IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureLogging((context, logging) =>
                logging.AddApplicationLogging(
                    context.Configuration,
                    context.HostingEnvironment.EnvironmentName))
            .ConfigureServices((context, services) =>
            {
                services.AddApplication();
                services.AddInfrastructure(context.Configuration);
                services.AddAppShell();
            })
            .Build();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        if (args.Exception is Exception exception)
        {
            var error = ExceptionMapper.Map(exception);
            _logger.LogCritical(
                exception,
                "Unhandled exception detected at the application boundary. {ErrorCode} ({ErrorType})",
                error.Code,
                error.Type);
        }
        else
        {
            _logger.LogCritical("Unhandled non-exception failure detected at the application boundary.");
        }

        // The exception is deliberately not marked handled. Without a user-facing error
        // surface yet, continuing could leave the app in an inconsistent state; the runtime
        // is allowed to terminate. A later UI step will add selective handling.
        args.Handled = false;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        _logger.LogWarning("Unobserved task exception detected.");
        _errorHandler.Handle(args.Exception, "UnobservedTaskException");
        args.SetObserved();
    }

    private async Task ShutdownHostAsync()
    {
        if (_hostStopped)
        {
            return;
        }

        _hostStopped = true;
        _logger.LogInformation("Application stopping.");

        try
        {
            await _host.StopAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Application host shutdown failed.");
        }
        finally
        {
            _host.Dispose();
            _logger.LogInformation("Application stopped.");
        }
    }
}
