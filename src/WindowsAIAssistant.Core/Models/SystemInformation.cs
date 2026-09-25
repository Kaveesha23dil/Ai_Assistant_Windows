namespace WindowsAIAssistant.Core.Models;

/// <summary>
/// Informational snapshot of the host Windows system.
/// </summary>
public sealed record SystemInformation
{
    public SystemInformation(
        string machineName,
        string operatingSystem,
        string operatingSystemVersion,
        string userName,
        int processorCount,
        long totalMemory,
        long availableMemory)
    {
        ArgumentNullException.ThrowIfNull(machineName);
        ArgumentNullException.ThrowIfNull(operatingSystem);
        ArgumentNullException.ThrowIfNull(userName);

        MachineName = machineName;
        OperatingSystem = operatingSystem;
        OperatingSystemVersion = operatingSystemVersion;
        UserName = userName;
        ProcessorCount = processorCount;
        TotalMemory = totalMemory;
        AvailableMemory = availableMemory;
    }

    /// <summary>Gets the machine (computer) name.</summary>
    public string MachineName { get; }

    /// <summary>Gets the operating system product name.</summary>
    public string OperatingSystem { get; }

    /// <summary>Gets the operating system version.</summary>
    public string OperatingSystemVersion { get; }

    /// <summary>Gets the current user name.</summary>
    public string UserName { get; }

    /// <summary>Gets the number of logical processors.</summary>
    public int ProcessorCount { get; }

    /// <summary>Gets the total physical memory in bytes.</summary>
    public long TotalMemory { get; }

    /// <summary>Gets the currently available memory in bytes.</summary>
    public long AvailableMemory { get; }
}