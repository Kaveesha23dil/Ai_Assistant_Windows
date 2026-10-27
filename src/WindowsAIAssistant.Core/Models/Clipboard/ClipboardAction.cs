using System.Collections.Generic;

namespace WindowsAIAssistant.Core.Models.Clipboard;

public class ClipboardAction
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public WritingAction ActionType { get; set; }
    public bool RequiresAI { get; set; } = true;
    public bool IsLocal { get; set; } = false;
    public int Order { get; set; }
}
