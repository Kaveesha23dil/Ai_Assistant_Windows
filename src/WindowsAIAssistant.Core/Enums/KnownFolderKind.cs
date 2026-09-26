namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Identifies a well-known user folder. Only the identifier crosses the Core boundary; the
/// concrete path is resolved by the platform-specific implementation so no user name or
/// machine-specific path is ever built in shared code.
/// </summary>
public enum KnownFolderKind
{
    /// <summary>The current user's Downloads folder.</summary>
    Downloads,

    /// <summary>The current user's Documents folder.</summary>
    Documents,

    /// <summary>The current user's Pictures folder.</summary>
    Pictures,

    /// <summary>The current user's Music folder.</summary>
    Music,

    /// <summary>The current user's Videos folder.</summary>
    Videos,

    /// <summary>The current user's Desktop folder.</summary>
    Desktop
}
