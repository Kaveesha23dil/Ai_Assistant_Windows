using WindowsAIAssistant.Application.Common.Mapping;
using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Abstractions.System;

namespace WindowsAIAssistant.Application.System.Queries.GetSystemInformation;

public sealed class GetSystemInformationHandler
{
    private readonly IWindowsSystemService _windowsSystemService;

    public GetSystemInformationHandler(IWindowsSystemService windowsSystemService)
    {
        ArgumentNullException.ThrowIfNull(windowsSystemService);
        _windowsSystemService = windowsSystemService;
    }

    public async Task<SystemInformationDto> HandleAsync(
        GetSystemInformationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var information = await _windowsSystemService
            .GetSystemInformationAsync(cancellationToken)
            .ConfigureAwait(false);

        return information.ToDto();
    }
}
