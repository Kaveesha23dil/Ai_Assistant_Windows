using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Core.Abstractions.System;

/// <summary>Reports the machine's battery charge and charging state.</summary>
public interface IBatteryService
{
    /// <summary>Gets a value indicating whether the machine has a battery.</summary>
    bool IsBatteryPresent { get; }

    /// <summary>Reads the current battery state.</summary>
    Task<Result<BatteryStatus>> GetStatusAsync(CancellationToken cancellationToken = default);
}
