using System.Globalization;
using Microsoft.Extensions.Configuration;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Infrastructure.Agents.Configuration;

/// <summary>
/// Binds the <c>Agent</c> section onto <see cref="AgentOptions"/> without letting an unusable
/// value stop the application from starting.
/// <para>
/// The ordinary <c>Bind</c> call throws on an empty string where an enum is expected, and it throws
/// from inside the options system, where a person editing a settings file would see a stack trace
/// instead of a sentence. A section that has been emptied out to clear a value is an ordinary thing
/// for a file to contain, so a blank is read as "not set" and the documented default stands.
/// </para>
/// <para>
/// An unrecognised name is treated the same way. Neither value can grant a capability: the mode
/// changes wording on one page, and the persistence setting chooses between two stores that obey
/// the same rules, so reading an unusable value as the default cannot make the agent do anything
/// the file had not already allowed.
/// </para>
/// </summary>
public static class AgentOptionsBinder
{
    /// <summary>Copies the configured values onto the options instance.</summary>
    public static void Bind(AgentOptions options, IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(section);

        var fileName = section["DatabaseFileName"];
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            options.DatabaseFileName = fileName;
        }

        var retention = section["ActivityRetentionCount"];
        if (int.TryParse(retention, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0)
        {
            options.ActivityRetentionCount = parsed;
        }

        options.Persistence = Enum.TryParse<AgentPersistenceMode>(
            section["Persistence"],
            ignoreCase: true,
            out var persistence)
            ? persistence
            : AgentPersistenceMode.Persistent;

        options.PresentationMode = Enum.TryParse<AgentPresentationMode>(
            section["PresentationMode"],
            ignoreCase: true,
            out var presentation)
            ? presentation
            : AgentPresentationMode.Standard;
    }

    /// <summary>
    /// Reads the persistence mode directly, for the registration that has to decide which stores to
    /// register before any options instance exists.
    /// </summary>
    public static AgentPersistenceMode ReadPersistence(IConfiguration root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return Enum.TryParse<AgentPersistenceMode>(
            root[$"{AgentOptions.SectionName}:Persistence"],
            ignoreCase: true,
            out var mode)
            ? mode
            : AgentPersistenceMode.Persistent;
    }
}
