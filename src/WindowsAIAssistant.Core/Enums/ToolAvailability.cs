namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Whether a tool can be used on this machine, right now, for this person.
/// <para>
/// The four states are kept apart because they call for four different answers. Unregistered
/// means the build has no such tool and the plan is wrong. Unavailable means the tool exists but
/// a capability switch is off, which the person can fix in Settings. Misconfigured means the
/// tool is present but something it needs is missing, such as a model that cannot accept an
/// image. Available means it can run.
/// </para>
/// </summary>
public enum ToolAvailability
{
    /// <summary>No such tool is registered. A plan naming it is refused.</summary>
    Unregistered = 0,

    /// <summary>
    /// The tool is registered but the capability it needs is not consented to, so running it
    /// would do nothing or would break a privacy promise.
    /// </summary>
    Unavailable = 1,

    /// <summary>The tool is present but is missing something it cannot work without.</summary>
    Misconfigured = 2,

    /// <summary>The tool can run.</summary>
    Available = 3
}
