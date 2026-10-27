using System;

namespace WindowsAIAssistant.Core.Models.Clipboard;

public class ClipboardItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ClipboardContent Content { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
