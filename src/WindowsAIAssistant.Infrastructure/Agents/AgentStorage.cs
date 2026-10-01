using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WindowsAIAssistant.Infrastructure.Agents;

/// <summary>
/// The small conversions between agent values and their stored form.
/// <para>
/// Kept apart from the schema and from the repositories so that both stores agree, byte for
/// byte, on how a timestamp is written and how an ordered list of tool names is joined. Two
/// copies of these three functions would be two chances for the timeline to read back a run
/// whose tools came out in a different order than they went in.
/// </para>
/// <para>
/// Round-trip ISO-8601 rather than Unix seconds, and text rather than a blob, for the same
/// reason the knowledge index does it: the file is something a person may reasonably open in a
/// database tool when they want to know what the assistant remembered, and it should be
/// readable rather than a puzzle.
/// </para>
/// </summary>
internal static class AgentStorage
{
    /// <summary>
    /// The separator for an ordered list in one column. A control character that cannot occur in
    /// a tool name, so the join round-trips without any escaping.
    /// </summary>
    /// <remarks>
    /// The same choice the knowledge schema makes for section names, and for the same reason:
    /// a character that cannot appear in the values keeps the join unambiguous without any
    /// escaping.
    /// </remarks>
    public const char ListSeparator = '\u001F';

    /// <summary>Writes an ordered list into one column.</summary>
    public static string ToStorage(this IReadOnlyList<string> value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Count == 0
            ? string.Empty
            : string.Join(ListSeparator.ToString(), value);
    }

    /// <summary>Reads an ordered list back out of one column.</summary>
    public static IReadOnlyList<string> ListFromStorage(string? value) =>
        string.IsNullOrEmpty(value)
            ? []
            : value.Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Writes a timestamp in round-trip form, always in UTC.</summary>
    public static string ToStorage(this DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Writes a timestamp in round-trip form, or null.</summary>
    public static string? ToStorage(this DateTimeOffset? value) => value?.ToStorage();

    /// <summary>Reads a timestamp written by <see cref="ToStorage(DateTimeOffset)"/>.</summary>
    /// <exception cref="FormatException">The column is not a timestamp this build wrote.</exception>
    public static DateTimeOffset ReadTimestamp(this SqliteDataReader reader, int ordinal) =>
        DateTimeOffset.Parse(
            reader.GetString(ordinal),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    /// <summary>Reads a nullable timestamp column.</summary>
    public static DateTimeOffset? ReadNullableTimestamp(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.ReadTimestamp(ordinal);

    /// <summary>Reads a nullable text column.</summary>
    public static string? ReadNullableText(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>Turns a nullable string into something that can be bound to a parameter.</summary>
    public static object ToParameter(this string? value) => (object?)value ?? DBNull.Value;

    /// <summary>
    /// Turns a nullable identifier into something that can be bound to a parameter.
    /// <para>
    /// An identifier is stored as text rather than as SQLite's blob format, so it reads back
    /// legibly in a database opened by hand. The round-trip cost is a parse on every read, which
    /// is not worth paying attention to at the rate a timeline grows.
    /// </para>
    /// </summary>
    public static object ToParameter(this Guid? value) =>
        value is { } identifier ? identifier.ToString() : DBNull.Value;
}