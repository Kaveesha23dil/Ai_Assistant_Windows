namespace WindowsAIAssistant.Application.Launcher;

/// <summary>
/// The launcher's search limits as an application-owned value. Infrastructure options are
/// mapped onto this at the composition root, keeping this layer independent of configuration.
/// </summary>
public sealed record LauncherSearchPolicy
{
    /// <summary>Gets the default number of rows shown at once.</summary>
    public int MaximumResults { get; init; } = 20;

    /// <summary>Gets how long a single provider may take before it is abandoned.</summary>
    public int ProviderTimeoutMilliseconds { get; init; } = 700;

    /// <summary>Gets how long input must settle before a search starts.</summary>
    public int DebounceMilliseconds { get; init; } = 180;
}
