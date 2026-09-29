using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Core.Models.Vision;

/// <summary>
/// A rectangle inside a captured frame, in that frame's own pixels.
/// <para>
/// Deliberately not screen coordinates. A person drags a rectangle on top of a preview, and the
/// preview has already been scaled, cropped, and placed across monitors with different scale
/// factors; carrying the number they dragged and re-deriving the crop from live screen
/// coordinates is how a highlight over one thing ends up cropping another. Once a frame exists,
/// the only coordinates that mean anything are that frame's own.
/// </para>
/// </summary>
public sealed record ScreenRegion
{
    public ScreenRegion(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Gets the left edge, in pixels from the left of the captured frame.</summary>
    public int X { get; }

    /// <summary>Gets the top edge, in pixels from the top of the captured frame.</summary>
    public int Y { get; }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets a value indicating whether the rectangle covers any pixels at all.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>Gets the right edge, exclusive, in captured-frame pixels.</summary>
    public int Right => X + Width;

    /// <summary>Gets the bottom edge, exclusive, in captured-frame pixels.</summary>
    public int Bottom => Y + Height;

    /// <summary>Gets the area in square pixels.</summary>
    public long Area => (long)Width * Height;

    /// <summary>Gets a rectangle covering the whole of a frame of the given size.</summary>
    public static ScreenRegion Full(int frameWidth, int frameHeight) =>
        new(0, 0, frameWidth, frameHeight);

    /// <summary>
    /// Brings a requested rectangle inside a frame, or explains why it cannot.
    /// <para>
    /// This is the only place a requested rectangle is turned into a crop, because it is the
    /// only place that knows the frame's size. A rectangle that is entirely outside the frame,
    /// or that is empty, or that is larger than the frame and has a negative origin, is refused
    /// rather than quietly repaired: a person who highlighted the wrong thing needs to be told
    /// they did, not shown an analysis of a rectangle they did not choose. A rectangle that
    /// merely overhangs an edge is trimmed, because that is plainly what was meant and the
    /// visible part is the part they asked about.
    /// </para>
    /// </summary>
    /// <param name="requested">The rectangle as chosen, in frame pixels.</param>
    /// <param name="frameWidth">The captured frame's width in pixels.</param>
    /// <param name="frameHeight">The captured frame's height in pixels.</param>
    /// <param name="region">The rectangle that may safely be cropped, or <see langword="null"/>.</param>
    /// <param name="error">A user-safe reason the rectangle was refused, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="region"/> may be cropped.</returns>
    public static bool TryNormalize(
        ScreenRegion? requested,
        int frameWidth,
        int frameHeight,
        out ScreenRegion? region,
        out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);

        region = null;
        error = null;

        if (requested is null)
        {
            error = ErrorCodes.ScreenRegionInvalid;
            return false;
        }

        if (requested.IsEmpty)
        {
            error = ErrorCodes.ScreenRegionEmpty;
            return false;
        }

        var left = Math.Max(0, requested.X);
        var top = Math.Max(0, requested.Y);
        var right = Math.Min(frameWidth, requested.Right);
        var bottom = Math.Min(frameHeight, requested.Bottom);

        if (right <= left || bottom <= top)
        {
            error = ErrorCodes.ScreenRegionOutsideFrame;
            return false;
        }

        region = new ScreenRegion(left, top, right - left, bottom - top);
        return true;
    }

    /// <summary>Returns a readable form, used in status text and never in a prompt or a log.</summary>
    public override string ToString() =>
        $"{Width}x{Height} at ({X},{Y})";
}
