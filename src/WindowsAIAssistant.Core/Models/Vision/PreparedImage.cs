using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Core.Models.Vision;

/// <summary>
/// An image after preparation, and what had to be done to it.
/// <para>
/// The size of the result and the flags below are the difference between "here is your screen"
/// and "here is a quarter of your screen, reduced, because the model will not take more". The
/// person is told which happened through <see cref="WasResized"/> rather than discovering it by
/// noticing that a serial number is unreadable.
/// </para>
/// </summary>
public sealed record PreparedImage
{
    public PreparedImage(
        byte[] imageBytes,
        int width,
        int height,
        bool wasResized = false,
        bool wasReencoded = false)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        ImageBytes = imageBytes;
        Width = width;
        Height = height;
        WasResized = wasResized;
        WasReencoded = wasReencoded;
    }

    /// <summary>Gets the encoded image.</summary>
    public byte[] ImageBytes { get; }

    /// <summary>Gets the width in pixels after preparation.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels after preparation.</summary>
    public int Height { get; }

    /// <summary>Gets a value indicating whether the image was scaled down.</summary>
    public bool WasResized { get; }

    /// <summary>Gets a value indicating whether the image was re-encoded.</summary>
    public bool WasReencoded { get; }

    /// <summary>Gets the size in bytes.</summary>
    public int SizeInBytes => ImageBytes.Length;
}
