namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Which optical character recognition implementation answered a request.
/// <para>
/// The order of these values is the order they are tried in: the newest, most capable engine
/// first, the fallback that has been in Windows for years second, and a no-op last so that
/// callers never have to ask whether recognition is possible before asking for it.
/// </para>
/// </summary>
public enum OcrProviderKind
{
    /// <summary>
    /// Nothing on this device can read text. Requests return empty results rather than failing,
    /// so a machine with no recogniser can still take part in image analysis.
    /// </summary>
    None = 0,

    /// <summary>
    /// The modern WinRT recogniser, whose model may need to be installed first.
    /// </summary>
    WindowsAi = 1,

    /// <summary>
    /// The built-in legacy recogniser, present on every supported version of Windows.
    /// </summary>
    WindowsLegacy = 2
}
