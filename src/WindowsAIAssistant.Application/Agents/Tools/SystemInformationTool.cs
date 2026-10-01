using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents.Tools;

/// <summary>
/// Reports what this computer is and how it is doing.
/// <para>
/// Read-only and entirely local — the operating system is asked directly and no provider is
/// involved — which is why a request for it can be answered even with every cloud feature
/// switched off. That matters more than it sounds: somebody diagnosing a slow machine is often
/// doing so precisely because the rest of the assistant is not working.
/// </para>
/// <para>
/// The user name is not included. It is on the machine and the service can see it, but it is
/// not something anybody asked to be told, and the account name of somebody looking over your
/// shoulder is not a detail to put in a log line.
/// </para>
/// </summary>
public sealed class SystemInformationTool : ITool
{
    private readonly IWindowsSystemService _system;
    private readonly IDriveSpaceService _driveSpace;
    private readonly ILogger<SystemInformationTool> _logger;

    public SystemInformationTool(
        IWindowsSystemService system,
        IDriveSpaceService driveSpace,
        ILogger<SystemInformationTool> logger)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(driveSpace);
        ArgumentNullException.ThrowIfNull(logger);

        _system = system;
        _driveSpace = driveSpace;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "SystemInformationTool";

    /// <inheritdoc />
    public string Description =>
        "Report the computer's name, operating system, processor count, memory, and disk use. " +
        "Use this for questions about this machine rather than about its files.";

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredInputs => [];

    /// <summary>
    /// Declares that this tool changes nothing. It reports what the machine already is — CPU,
    /// memory, drives, uptime — and reads it without altering it.
    /// </summary>
    public IReadOnlyList<AgentAction> DescribeActions(AgentStep step) => [];

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var information = await _system.GetSystemInformationAsync(cancellationToken);

            // Disk is a separate service and may fail on its own — a locked or absent volume says
            // nothing about whether the rest of the report is true, so a failure here drops the
            // disk line rather than the answer.
            var space = await TryReadDriveSpaceAsync(cancellationToken);

            stopwatch.Stop();

            return ToolResult.Success(
                Name,
                BuildText(information, space),
                sources: null,
                data: BuildData(information, space),
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not read the system information.");

            return ToolResult.Failure(
                Name,
                ErrorCodes.AgentStepFailed,
                "I could not read this computer's details just now.");
        }
    }

    /// <summary>
    /// Reads the system drive, treating a failure as "no information" rather than as a failure
    /// of the whole tool.
    /// </summary>
    private async Task<DriveSpace?> TryReadDriveSpaceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _driveSpace.GetSystemDriveSpaceAsync(cancellationToken);

            return result.IsSuccess ? result.Value : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not read the system drive space.");
            return null;
        }
    }

    /// <summary>
    /// Writes the facts as sentences. Every number is rounded to something readable, because a
    /// precise byte count is not more useful to a person and is more likely to be misread aloud.
    /// </summary>
    private static string BuildText(SystemInformation information, DriveSpace? space)
    {
        ArgumentNullException.ThrowIfNull(information);

        var memoryUsed = information.TotalMemory - information.AvailableMemory;
        var memoryPercent = information.TotalMemory > 0
            ? memoryUsed * 100 / information.TotalMemory
            : 0;

        return string.Join(
            "\n",
            $"This computer is called {information.MachineName}.",
            $"It is running {information.OperatingSystem} {information.OperatingSystemVersion}.".Trim(),
            $"It has {information.ProcessorCount.ToString(CultureInfo.InvariantCulture)} processor core(s).",
            $"It has {Bytes(information.TotalMemory)} of memory, " +
            $"with {Bytes(information.AvailableMemory)} free " +
            $"({memoryPercent.ToString(CultureInfo.InvariantCulture)}% in use).",
            Disk(space));
    }

    /// <summary>Reports disk use when the platform told us about it.</summary>
    private static string Disk(DriveSpace? space) =>
        space is null
            ? "I could not read the disk size."
            : $"Its system drive holds {Bytes(space.TotalBytes)}, " +
              $"with {Bytes(space.AvailableBytes)} free.";

    /// <summary>Builds the metadata. Numbers only — no paths, no account name, nothing read.</summary>
    private static IReadOnlyDictionary<string, string> BuildData(SystemInformation information, DriveSpace? space)
    {
        ArgumentNullException.ThrowIfNull(information);

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["os"] = information.OperatingSystem,
            ["osVersion"] = information.OperatingSystemVersion,
            ["processorCount"] = information.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            ["totalMemoryBytes"] = information.TotalMemory.ToString(CultureInfo.InvariantCulture),
            ["availableMemoryBytes"] = information.AvailableMemory.ToString(CultureInfo.InvariantCulture),
            ["summary"] =
                $"{information.MachineName} — {information.OperatingSystem}, " +
                $"{information.ProcessorCount.ToString(CultureInfo.InvariantCulture)} core(s), " +
                $"{Bytes(information.AvailableMemory)} of {Bytes(information.TotalMemory)} memory free.",
        };

        if (space is not null)
        {
            data["drive"] = space.DriveName;
            data["totalDiskBytes"] = space.TotalBytes.ToString(CultureInfo.InvariantCulture);
            data["availableDiskBytes"] = space.AvailableBytes.ToString(CultureInfo.InvariantCulture);
        }

        return data;
    }

    /// <summary>Renders a byte count in binary units, which is what Windows reports in.</summary>
    private static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
            : $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}
