using System.IO;

namespace WindowsAIAssistant.Application.Tests.Helpers;

/// <summary>
/// A settings file that is guaranteed not to exist.
/// <para>
/// The production composition reads the settings of the person signed in to the machine, so a
/// test that reused that path would quietly inherit whatever the person who ran it had saved,
/// and a voice test could pass or fail depending on their microphone switch. Every test points
/// the composition at this path instead.
/// </para>
/// <para>
/// The name carries a fresh identifier for each test run, so a file left behind by a crashed
/// run can never be picked up by the next one.
/// </para>
/// </summary>
internal static class AbsentSettingsFile
{
    public static string FilePath { get; } = Path.Combine(
        Path.GetTempPath(),
        "WindowsAIAssistant.Tests",
        $"absent-{Guid.NewGuid():N}.json");
}
