namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// How much damage a step could do if it were wrong, which decides whether a person is asked
/// before it runs.
/// <para>
/// Separate from <see cref="ActionSafetyLevel"/>, which answers a different question. That enum
/// is about a single spoken command: is this safe, does it need confirming, or is it refused
/// outright. This one is about a step inside a multi-step plan, where the person approved a goal
/// rather than a command, and the question is how far the plan is allowed to travel on that
/// approval alone.
/// </para>
/// <para>
/// The levels are cumulative in force rather than in value: a <see cref="High"/> step is never
/// run without an answer, even in a plan whose other steps are all <see cref="Low"/>.
/// </para>
/// </summary>
public enum ActionRiskLevel
{
    /// <summary>
    /// Reads only. Searching documents, reading a screen, writing a summary: nothing on the
    /// machine changes, and nothing a person would want back is lost.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Creates or moves something. Writing a report file, renaming a document: the person can
    /// undo it, but it is a change to their disk and they are told first.
    /// </summary>
    Medium = 1,

    /// <summary>
    /// Destroys something or reaches outside the application. Deleting a file, changing a system
    /// setting, sending a document to a provider it was not intended for.
    /// </summary>
    High = 2
}
