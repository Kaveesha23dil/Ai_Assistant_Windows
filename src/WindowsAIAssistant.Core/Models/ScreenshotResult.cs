namespace WindowsAIAssistant.Core.Models;

/// <summary>The outcome of a screen capture.</summary>
public sealed record ScreenshotResult
{
    public ScreenshotResult(string filePath, long sizeInBytes, DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        FilePath = filePath;
        SizeInBytes = sizeInBytes;
        CapturedAt = capturedAt;
    }

    /// <summary>Gets the absolute path of the saved image.</summary>
    public string FilePath { get; }

    /// <summary>Gets the size of the saved image in bytes.</summary>
    public long SizeInBytes { get; }

    /// <summary>Gets the point in time the image was captured.</summary>
    public DateTimeOffset CapturedAt { get; }
}
