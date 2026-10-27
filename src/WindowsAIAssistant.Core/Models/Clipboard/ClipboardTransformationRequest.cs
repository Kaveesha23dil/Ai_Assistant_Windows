using System;

namespace WindowsAIAssistant.Core.Models.Clipboard;

public class ClipboardTransformationRequest
{
    public ClipboardContent Content { get; set; } = new();
    public WritingAction Action { get; set; }
    public WritingTone Tone { get; set; } = WritingTone.Neutral;
    public string? TargetLanguage { get; set; }
    public string? UserInstruction { get; set; }
    public string? SourceLanguage { get; set; }
    public bool AllowCloud { get; set; } = false;
}
