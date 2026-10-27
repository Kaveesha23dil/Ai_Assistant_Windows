using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.App.ViewModels.Clipboard;

public sealed partial class ClipboardAssistantViewModel : ObservableObject
{
    private readonly IClipboardService _clipboardService;
    private readonly IClipboardContentAnalyzer _analyzer;
    private readonly IWritingAssistantService _writingAssistant;

    [ObservableProperty]
    public partial string PreviewText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetectedType { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int Length { get; set; }

    [ObservableProperty]
    public partial bool HasContent { get; set; }

    [ObservableProperty]
    public partial string ResultText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasResult { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<ClipboardAction> Actions { get; } = new();

    private ClipboardContent? _currentContent;

    public ClipboardAssistantViewModel(IClipboardService clipboardService, IClipboardContentAnalyzer analyzer, IWritingAssistantService writingAssistant)
    {
        _clipboardService = clipboardService;
        _analyzer = analyzer;
        _writingAssistant = writingAssistant;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var content = await _clipboardService.GetCurrentContentAsync(cancellationToken).ConfigureAwait(true);
            _currentContent = await _analyzer.AnalyzeAsync(content, cancellationToken).ConfigureAwait(true);

            DetectedType = _currentContent.Type.ToString();
            Length = _currentContent.Length;
            HasContent = _currentContent.HasText || _currentContent.HasImage || _currentContent.HasFiles;

            if (_currentContent.HasText && _currentContent.Text != null)
            {
                PreviewText = _currentContent.Text.Length > 300 ? _currentContent.Text.Substring(0, 300) + "..." : _currentContent.Text;
            }
            else if (_currentContent.HasFiles)
            {
                PreviewText = string.Join(Environment.NewLine, _currentContent.FilePaths);
            }
            else if (_currentContent.HasImage)
            {
                PreviewText = "[Image in clipboard]";
            }

            Actions.Clear();
            foreach (var action in _writingAssistant.GetAvailableActions(_currentContent.Type))
            {
                Actions.Add(action);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task TransformAsync(ClipboardAction action, CancellationToken cancellationToken = default)
    {
        if (_currentContent == null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var request = new ClipboardTransformationRequest
            {
                Content = _currentContent,
                Action = action.ActionType,
                AllowCloud = false
            };

            var result = await _writingAssistant.TransformAsync(request, cancellationToken).ConfigureAwait(true);
            if (result.Success && !string.IsNullOrEmpty(result.ResultText))
            {
                ResultText = result.ResultText;
                HasResult = true;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CopyResultAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ResultText))
        {
            return;
        }
        await _clipboardService.WriteTextAsync(ResultText, cancellationToken).ConfigureAwait(true);
    }
}
