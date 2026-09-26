using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.System;

/// <summary>
/// Reports free and total space for the machine's system drive.
/// <para>
/// Disk space is deliberately separate from the general system information service so that
/// reporting capacity and reporting CPU or memory stay independently replaceable, and so a
/// handler never has to parse a formatted system summary to get a single number.
/// </para>
/// </summary>
public interface IDriveSpaceService
{
    /// <summary>Reads the space available on the drive that holds the operating system.</summary>
    Task<Result<DriveSpace>> GetSystemDriveSpaceAsync(CancellationToken cancellationToken = default);
}
