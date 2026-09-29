namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// What a person is asking to be done with what is on their screen.
/// <para>
/// The value reaches the model as a sentence describing the task rather than as a code, so the
/// distinction is a decision made in the Application layer where it can be read. It is kept
/// separate from <see cref="ScreenCaptureType"/> because what was captured and what is being
/// asked about it are different questions, and a person can ask the same thing about a whole
/// window or a five-hundred-pixel rectangle.
/// </para>
/// </summary>
public enum ScreenAnalysisType
{
    /// <summary>Say what is visible. The default for "what's on my screen".</summary>
    Describe = 0,

    /// <summary>Condense what is visible into a short summary.</summary>
    Summarize = 1,

    /// <summary>Explain what is visible in general terms.</summary>
    Explain = 2,

    /// <summary>
    /// Return the visible text.
    /// <para>
    /// Served locally by optical character recognition wherever the device can do it, so this
    /// one does not need a model at all and works with every cloud switch turned off.
    /// </para>
    /// </summary>
    ExtractText = 3,

    /// <summary>Explain an error message, dialog, or warning that is visible.</summary>
    ExplainError = 4,

    /// <summary>Explain what a dialog or window is asking the person to do.</summary>
    ExplainUI = 5,

    /// <summary>Describe a chart, graph, or plot that is visible.</summary>
    AnalyzeChart = 6,

    /// <summary>Explain source code that is visible.</summary>
    AnalyzeCode = 7,

    /// <summary>Answer a question the person asked about what is visible.</summary>
    AnswerQuestion = 8
}
