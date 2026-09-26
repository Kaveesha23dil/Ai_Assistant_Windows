using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Abstractions.Security;

/// <summary>
/// Answers whether a capability may be used right now and, when it may not, supplies the
/// exact sentence to speak back.
/// <para>
/// Every handler consults this before touching a system service. Centralizing the consent
/// switches is what makes "no dangerous command bypasses a permission check" a property of
/// the design rather than a convention.
/// </para>
/// </summary>
public interface IPermissionService
{
    /// <summary>Returns whether the capability is currently consented to.</summary>
    bool IsGranted(PermissionCapability capability);

    /// <summary>Returns the user-safe sentence explaining why a capability is unavailable.</summary>
    string GetDeniedMessage(PermissionCapability capability);

    /// <summary>
    /// Returns a successful result when the capability is consented to, otherwise a failure
    /// carrying the denied message.
    /// </summary>
    Result EnsureGranted(PermissionCapability capability);
}
