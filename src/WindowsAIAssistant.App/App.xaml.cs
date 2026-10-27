```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

// Keep your existing namespace here
namespace WindowsAIAssistant
{
    public partial class App : Application
    {
        private readonly IHost _host;
        private readonly ILogger<App> _logger;
        private readonly IErrorHandler _errorHandler;

        private MainWindow? _window;
        private LauncherWindowHost? _launcherHost;

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

                var environment = _host.Services
                    .GetRequiredService<IHostEnvironment>()
                    .EnvironmentName;

                _logger.LogInformation(
                    "Environment detected: {Environment}.",
                    environment);

                var aiOptions = _host.Services
                    .GetRequiredService<IOptions<AIOptions>>()
                    .Value;

                _logger.LogInformation(
                    "AI provider type: {Provider}.",
                    aiOptions.Provider);

                _logger.LogInformation("Application services configured.");

                await _host.StartAsync();

                _logger.LogInformation("Application started successfully.");

                // Resolved merge conflict:
                // Keep the local variable because it is used below,
                // and keep the automation event registration from main.
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                _window = mainWindow;

                _host.Services
                    .GetRequiredService<Core.Abstractions.Automations.IAutomationService>()
                    .Completed += OnAutomationCompleted;

                _window.Closed += (_, _) => _ = ShutdownHostAsync();
                _window.Activate();

                // The launcher is started after the shell is on screen so a shortcut conflict can
                // be explained in a dialog with a XamlRoot. A failure is never allowed to stop the
                // application: the shell remains usable without the global shortcut.
                _launcherHost = _host.Services.GetRequiredService<LauncherWindowHost>();

                var launcherRegistration = await _launcherHost.StartAsync();

                if (launcherRegistration.IsFailure)
                {
                    await mainWindow.ShowLauncherShortcutErrorAsync(
                        launcherRegistration.ErrorMessage
                        ?? "The launcher shortcut is unavailable.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to launch Windows AI Assistant.");

                await _errorHandler.HandleAsync(ex);
            }
        }

        private void OnUnhandledException(
            object sender,
            Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            _logger.LogError(
                e.Exception,
                "Unhandled application exception.");

            _ = _errorHandler.HandleAsync(e.Exception);

            e.Handled = true;
        }

        private void OnUnobservedTaskException(
            object? sender,
            UnobservedTaskExceptionEventArgs e)
        {
            _logger.LogError(
                e.Exception,
                "Unobserved task exception.");

            _ = _errorHandler.HandleAsync(e.Exception);

            e.SetObserved();
        }

        private void OnAutomationCompleted(
            object? sender,
            EventArgs e)
        {
            _logger.LogInformation("Automation completed.");
        }

        private async Task ShutdownHostAsync()
        {
            try
            {
                if (_launcherHost is not null)
                {
                    await _launcherHost.StopAsync();
                }

                await _host.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error while shutting down the application.");
            }
        }

        private static IHost CreateHost()
        {
            return Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Keep your existing service registrations here.
                    //
                    // Example:
                    // services.Configure<AIOptions>(
                    //     context.Configuration.GetSection("AI"));
                    //
                    // services.AddSingleton<MainWindow>();
                    // services.AddSingleton<LauncherWindowHost>();
                    // services.AddSingleton<IErrorHandler, ErrorHandler>();
                    // services.AddSingleton<IAutomationService, AutomationService>();
                })
                .Build();
        }
    }
}
```

**Important:** I had to infer the parts of the file that were not included in your message, such as the exact namespace, `CreateHost()` registrations, and the actual signatures of `IErrorHandler` and `OnAutomationCompleted`. Do **not** replace those parts blindly if your original file already contains them.

The actual merge conflict itself should be resolved exactly like this:

```csharp
var mainWindow = _host.Services.GetRequiredService<MainWindow>();
_window = mainWindow;

_host.Services
    .GetRequiredService<Core.Abstractions.Automations.IAutomationService>()
    .Completed += OnAutomationCompleted;
```

Then remove all `<<<<<<<`, `=======`, and `>>>>>>>` markers.