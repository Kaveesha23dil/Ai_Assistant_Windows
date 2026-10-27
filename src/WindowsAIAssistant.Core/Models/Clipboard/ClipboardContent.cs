using System;
using System.Collections.Generic;

namespace WindowsAIAssistant.Core.Models.Clipboard;

public class ClipboardContent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ClipboardContentType Type { get; set; } = ClipboardContentType.Unknown;
    public string? Text { get; set; }
    public byte[]? ImageData { get; set; }
    public string? ImageFormat { get; set; }
    public List<string> FilePaths { get; set; } = new();
    public List<string> AvailableFormats { get; set; } = new();
    public int Length => Text?.Length ?? 0;
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public bool HasText => !string.IsNullOrEmpty(Text);
    public bool HasImage => ImageData != null && ImageData.Length > 0;
    public bool HasFiles => FilePaths.Count > 0;
}
