using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Application.Tests.Fakes;

/// <summary>
/// A stand-in shell launcher that records what it was asked to open.
/// <para>
/// The real one hands a URI to Windows. What a test needs to know is which address was asked
/// for, and that is decided entirely by the caller: this fake opens nothing and never could.
/// </para>
/// </summary>
public sealed class FakeUriLauncher(List<Uri> opened) : IUriLauncherService
{
    public List<Uri> Opened { get; } = opened;

    public Result Result { get; set; } = Result.Success();

    public Task<Result> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Result.IsFailure)
        {
            return Task.FromResult(Result);
        }

        Opened.Add(uri);
        return Task.FromResult(Result.Success());
    }
}
