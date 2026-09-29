using WindowsAIAssistant.Core.Abstractions.Vision;
using WindowsAIAssistant.Core.Models.Vision;

namespace WindowsAIAssistant.Application.Vision;

/// <summary>
/// Holds the one screenshot the person is currently talking about.
/// <para>
/// A single slot, and the reason is the same as the interface's: a list would be a history. A
/// history of screenshots outlives the request that produced it, is readable by anything holding
/// this object, and was never asked for. One frame, replaced per request and forgotten on
/// request, is a thing a person can hold in their head.
/// </para>
/// <para>
/// The image is not stored here. This object keeps a description, the recognized text, and the
/// consent that was in force — enough to render a chip, to ground a follow-up, and to honour the
/// original decision rather than today's settings — while the bytes stay with the caller that
/// captured them and are erased by it. A singleton that held a screenshot buffer would be a
/// screenshot that outlives every screen lock and every sign-out.
/// </para>
/// </summary>
public sealed class ScreenContextService : IScreenContextService
{
    private readonly object _gate = new();
    private VisualSourceReference? _source;
    private string? _text;
    private bool _allowsCloud;

    /// <inheritdoc />
    public event EventHandler? ContextChanged;

    /// <inheritdoc />
    public bool HasScreen
    {
        get
        {
            lock (_gate)
            {
                return _source is not null;
            }
        }
    }

    /// <inheritdoc />
    public VisualSourceReference? Source
    {
        get
        {
            lock (_gate)
            {
                return _source;
            }
        }
    }

    /// <inheritdoc />
    public string? ExtractedText
    {
        get
        {
            lock (_gate)
            {
                return _text;
            }
        }
    }

    /// <inheritdoc />
    public bool AllowsCloudSubmission
    {
        get
        {
            lock (_gate)
            {
                return _allowsCloud;
            }
        }
    }

    /// <inheritdoc />
    public void Hold(VisualSourceReference source, string? extractedText, bool allowsCloudSubmission)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_gate)
        {
            _source = source;
            _text = string.IsNullOrWhiteSpace(extractedText) ? null : extractedText;
            _allowsCloud = allowsCloudSubmission;
        }

        ContextChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Clear()
    {
        bool hadContent;

        lock (_gate)
        {
            hadContent = _source is not null;
            _source = null;
            _text = null;
            _allowsCloud = false;
        }

        // Only raise when something was actually held, so a caller that clears defensively on
        // every navigation does not make every page redraw itself for nothing.
        if (hadContent)
        {
            ContextChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
