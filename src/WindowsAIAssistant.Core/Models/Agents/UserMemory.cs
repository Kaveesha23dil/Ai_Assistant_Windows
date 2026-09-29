using System.Globalization;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Agents;

/// <summary>
/// Something the assistant has been told to remember between runs.
/// <para>
/// The shape is deliberately narrow: a key, a value, and two timestamps. There is no field for
/// an attached document, a screen image, a credential, or a transcript, so the storage layer has
/// no column to put one in and no call site that would have to remember not to. The rules about
/// what may be a value at all are in <see cref="AgentMemoryRules"/>, and they are enforced on
/// the way in rather than trusted to every caller.
/// </para>
/// </summary>
public sealed record UserMemory
{
    public UserMemory(
        Guid id,
        AgentMemoryType memoryType,
        string key,
        string value,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Id = id;
        MemoryType = memoryType;
        Key = key.Trim();
        Value = value.Trim();
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Gets the identifier of this memory.</summary>
    public Guid Id { get; }

    /// <summary>Gets what kind of thing is being remembered.</summary>
    public AgentMemoryType MemoryType { get; }

    /// <summary>
    /// Gets the stable name this memory is stored under, compared without regard to case. Two
    /// preferences of the same kind share a key deliberately: a person who changes their mind
    /// about a report format is updating it, not accumulating a second one.
    /// </summary>
    public string Key { get; }

    /// <summary>Gets what was remembered. Never a document, a screen, or a credential.</summary>
    public string Value { get; init; }

    /// <summary>Gets when it was first remembered.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets when it was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Gets how many times it has been used, so a stale preference can be noticed.</summary>
    public int UseCount { get; init; }

    /// <summary>Returns this memory with a new value, keeping when it was first remembered.</summary>
    public UserMemory WithValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return this with { Value = value.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
    }

    /// <summary>Returns this memory with its use counted.</summary>
    public UserMemory AsUsed() => this with { UseCount = UseCount + 1 };

    /// <summary>Records a preference.</summary>
    public static UserMemory Preference(string key, string value) =>
        Create(AgentMemoryType.UserPreference, key, value);

    /// <summary>Records a preference about the assistant's own behaviour.</summary>
    public static UserMemory AssistantPreference(string key, string value) =>
        Create(AgentMemoryType.AssistantPreference, key, value);

    /// <summary>Records that a piece of work was done.</summary>
    public static UserMemory Workflow(string key, string value) =>
        Create(AgentMemoryType.WorkflowHistory, key, value);

    /// <summary>Creates a memory of any kind, stamped with the current time.</summary>
    public static UserMemory Create(AgentMemoryType memoryType, string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var now = DateTimeOffset.UtcNow;
        return new UserMemory(Guid.NewGuid(), memoryType, key, value, now, now);
    }

    /// <summary>Gets a one-line form for a log entry, carrying the value.</summary>
    /// <remarks>
    /// Safe because <see cref="AgentMemoryRules"/> has already refused anything that looks like a
    /// credential, so a value reaching this point is a preference and not a secret.
    /// </remarks>
    public override string ToString() =>
        $"{MemoryType}:{Key} = {Value} (used {UseCount.ToString(CultureInfo.InvariantCulture)}x)";
}

/// <summary>
/// What the agent is allowed to put in memory, decided in one place.
/// <para>
/// These rules are the reason memory is safe to persist at all. They are applied by the service
/// on the way in, so a tool that tried to store a passphrase would be refused — the tool is not
/// trusted to have remembered, and neither is the planner that asked it to.
/// </para>
/// <para>
/// The test is on the shape of the value rather than on who is asking: a memory longer than
/// <see cref="MaximumValueLength"/> is refused because no preference is that long and a
/// document paragraph might be, and any value carrying a credential marker is refused outright
/// because no preference is ever one.
/// </para>
/// </summary>
public static class AgentMemoryRules
{
    /// <summary>The longest value a memory may hold.</summary>
    public const int MaximumValueLength = 400;

    /// <summary>The longest key a memory may be stored under.</summary>
    public const int MaximumKeyLength = 120;

    /// <summary>
    /// The words that mark a value as a credential. A match refuses the memory rather than
    /// redacting it: somebody who told the assistant their password has made a mistake, and the
    /// right response is to have stored nothing at all.
    /// </summary>
    private static readonly string[] CredentialMarkers =
    [
        "password",
        "passwd",
        "api key",
        "api-key",
        "apikey",
        "secret",
        "bearer ",
        "authorization:",
        "credential",
        "private key",
        "access token",
        "refresh token",
    ];

    /// <summary>Reports whether a value may be remembered, and says why when it may not.</summary>
    public static bool IsAllowed(string? value, out string? refusal)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            refusal = "There is nothing worth remembering in that.";
            return false;
        }

        if (value.Length > MaximumValueLength)
        {
            refusal =
                $"That is too long to remember as a preference " +
                $"(the limit is {MaximumValueLength} characters).";
            return false;
        }

        if (MentionsCredential(value))
        {
            refusal =
                "That looks like a password or a key, so I have not saved it. " +
                "Keep credentials in the place they belong, not in a chat.";
            return false;
        }

        refusal = null;
        return true;
    }

    /// <summary>Reports whether a value carries anything that looks like a credential.</summary>
    public static bool MentionsCredential(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lowered = value.ToLowerInvariant();
        return CredentialMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>Reports whether a key is a usable storage key.</summary>
    public static bool IsValidKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && key.Trim().Length <= MaximumKeyLength;
}
