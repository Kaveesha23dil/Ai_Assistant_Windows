using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAIAssistant.App.ViewModels.Clipboard;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.App.Views.Clipboard;

public sealed partial class ClipboardAssistantView : Page
{
    public ClipboardAssistantViewModel ViewModel { get; }

    public ClipboardAssistantView()
    {
        // Try to resolve via App host if available
        try
        {
            var app = (dynamic)Microsoft.UI.Xaml.Application.Current;
            var host = app.Host;
            var services = (IServiceProvider)host.Services;
            ViewModel = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<WindowsAIAssistant.App.ViewModels.Clipboard.ClipboardAssistantViewModel>(services);
        }
        catch
        {
            // Fallback - create view model directly with defaults
            var clipboard = new WindowsAIAssistant.Infrastructure.Windows.WindowsClipboardService();
            var analyzer = new WindowsAIAssistant.Infrastructure.Clipboard.ClipboardContentAnalyzer();
            var textTransform = new WindowsAIAssistant.Infrastructure.Clipboard.TextTransformationService();
            var writing = new WindowsAIAssistant.Infrastructure.Clipboard.WritingAssistantService(null, textTransform);
            ViewModel = new WindowsAIAssistant.App.ViewModels.Clipboard.ClipboardAssistantViewModel(clipboard, analyzer, writing);
        }
        this.InitializeComponent();
        this.Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }

    private async void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ClipboardAction action)
        {
            await ViewModel.TransformAsync(action);
        }
    }

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.CopyResultAsync();
    }

    private void OnReplaceClick(object sender, RoutedEventArgs e)
    {
    }

    private void OnInsertClick(object sender, RoutedEventArgs e)
    {
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ResultText = string.Empty;
    }
}
