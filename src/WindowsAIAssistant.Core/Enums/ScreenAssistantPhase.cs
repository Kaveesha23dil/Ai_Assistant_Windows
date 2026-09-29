namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Where a visual-assistant request is in its life.
/// <para>
/// These are states rather than a chain of callbacks so that the person can always be told
/// what is happening, so that a cancelled request lands in <see cref="Idle"/> rather than
/// <see cref="Error"/>, and so that nothing has to guess whether a half-finished capture is
/// still being held.
/// </para>
/// </summary>
public enum ScreenAssistantPhase
{
    /// <summary>Nothing in progress. The resting state, and where cancellation always returns.</summary>
    Idle = 0,

    /// <summary>The Windows capture picker is open, waiting for the person to choose a target.</summary>
    SelectingTarget = 1,

    /// <summary>A frame has been asked for and is on its way.</summary>
    Capturing = 2,

    /// <summary>A region overlay is open, waiting for the person to drag a rectangle.</summary>
    SelectingRegion = 3,

    /// <summary>Optical character recognition or image preparation is running.</summary>
    Processing = 4,

    /// <summary>
    /// A model is answering.
    /// <para>
    /// Reached only after both consents are confirmed, so its presence in a log or a crash
    /// report is itself evidence that nothing was sent without permission.
    /// </para>
    /// </summary>
    Analyzing = 5,

    /// <summary>An answer is ready and nothing further is happening.</summary>
    Completed = 6,

    /// <summary>Something failed in a way the person has to be told about.</summary>
    Error = 7
}
