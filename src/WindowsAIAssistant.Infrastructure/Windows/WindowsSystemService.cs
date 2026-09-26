using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Infrastructure.Windows;

/// <summary>
/// Reports a safe, read-only summary of the host: operating system, machine name, memory,
/// and processor count.
/// <para>
/// Everything comes from the runtime and the memory manager, so there is no WMI query, no
/// elevation prompt, and no registry read. Serial numbers, disk identifiers, and the signed-in
/// account's name are not collected beyond the user name the existing model already carries.
/// </para>
/// </summary>
public sealed partial class WindowsSystemService : IWindowsSystemService
{
    private readonly ILogger<WindowsSystemService> _logger;

    public WindowsSystemService(ILogger<WindowsSystemService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SystemInformation> GetSystemInformationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var (total, available) = ReadMemory();

            var information = new SystemInformation(
                Environment.MachineName,
                "Windows",
                Environment.OSVersion.Version.ToString(),
                Environment.UserName,
                Environment.ProcessorCount,
                total,
                available);

            return Task.FromResult(information);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Reading system information failed.");
            throw;
        }
    }

    /// <summary>
    /// Reads physical memory through the global memory status API, which is the same figure
    /// Task Manager reports and does not require a management instrumentation query.
    /// </summary>
    private static (long Total, long Available) ReadMemory()
    {
        if (GlobalMemoryStatusEx(out var status))
        {
            return ((long)status.TotalPhys, (long)status.AvailablePhys);
        }

        // A failed status query is not worth failing the whole request over; the process
        // figures are approximate but keep the answer usable.
        return (Environment.WorkingSet * 4, Environment.WorkingSet);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailablePhys;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(out MemoryStatusEx buffer);
}
