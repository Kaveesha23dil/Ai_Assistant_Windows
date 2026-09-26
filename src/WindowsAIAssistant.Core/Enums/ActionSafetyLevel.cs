namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// How dangerous an assistant action is, which determines whether it may execute
/// immediately, needs explicit confirmation, or must be refused altogether.
/// </summary>
public enum ActionSafetyLevel
{
    /// <summary>
    /// Read-only or easily reversible, for example reading the time or searching the web.
    /// Executes immediately.
    /// </summary>
    Safe,

    /// <summary>
    /// Changes something the user would notice losing, for example closing an application or
    /// changing a system setting. Requires an explicit confirmation step.
    /// </summary>
    ConfirmationRequired,

    /// <summary>
    /// Arbitrary code execution, registry editing, software installation, or recursive
    /// deletion. The voice assistant refuses these outright and has no handler for them.
    /// </summary>
    Restricted
}
