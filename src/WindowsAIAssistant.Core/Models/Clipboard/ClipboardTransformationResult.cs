using System;

namespace WindowsAIAssistant.Core.Models.Clipboard;

public class ClipboardTransformationResult
{
    public bool Success { get; set; }
    public string? ResultText { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsFromCache { get; set; }
    public TimeSpan Duration { get; set; }
    public bool RequiresUserConfirmation { get; set; } = true;
    public bool WasCancelled { get; set; }
}
