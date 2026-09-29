using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Core.Exceptions;

/// <summary>
/// An image could not be cropped, scaled, or encoded.
/// <para>
/// Separate from <see cref="ScreenVisionException"/> because these are arithmetic and encoding
/// failures with a cause in the bytes rather than a decision about consent, and because a
/// region that cannot be applied is something the person chose wrong and can choose again, while
/// a capture that was refused is something they may not be able to change at all.
/// </para>
/// </summary>
public class ImageProcessingException : ScreenVisionException
{
    public ImageProcessingException()
    {
    }

    public ImageProcessingException(string message)
        : base(message)
    {
    }

    public ImageProcessingException(string message, string errorCode)
        : base(message, errorCode)
    {
    }

    public ImageProcessingException(string message, string errorCode, Exception? innerException)
        : base(message, errorCode, innerException)
    {
    }
}
