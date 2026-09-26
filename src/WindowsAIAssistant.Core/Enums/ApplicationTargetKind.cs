namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Describes how a resolved application target must be launched. This keeps platform launch
/// mechanics out of Core and stops a raw user string from ever reaching the shell.
/// </summary>
public enum ApplicationTargetKind
{
    /// <summary>An executable or document path handed to the Windows shell.</summary>
    FilePath,

    /// <summary>A URI such as a Windows Settings address.</summary>
    Uri,

    /// <summary>A Windows Store application user model ID.</summary>
    AppUserModelId
}
