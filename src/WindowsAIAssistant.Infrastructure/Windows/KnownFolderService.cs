using System.Runtime.InteropServices;
using WindowsAIAssistant.Core.Abstractions.System;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Infrastructure.Windows;

/// <summary>
/// Resolves a well-known user folder through the shell's own known-folder API.
/// <para>
/// The identifier is mapped to a GUID and the path comes back from Windows. Nothing here
/// composes a path from a user name or a localized folder label, which is what keeps the
/// result correct on a redirected or OneDrive-backed profile.
/// </para>
/// </summary>
public sealed class KnownFolderService : IKnownFolderService
{
    private const int BufferSize = 32768;

    private static readonly IReadOnlyDictionary<KnownFolderKind, Guid> FolderIds =
        new Dictionary<KnownFolderKind, Guid>
        {
            [KnownFolderKind.Downloads] = new("374de290-123f-4565-9164-39c4925e467b"),
            [KnownFolderKind.Documents] = new("fdd39ad0-238f-46af-adb4-6c85480369c7"),
            [KnownFolderKind.Pictures] = new("33e28130-4e1e-4676-835a-98395c3bc3bb"),
            [KnownFolderKind.Music] = new("4bd8d571-6d19-48d3-be97-422220080e43"),
            [KnownFolderKind.Videos] = new("18989b1d-99b5-455b-841c-ab7c74e4ddfc"),
            [KnownFolderKind.Desktop] = new("b4bfcc3a-db2c-424c-b029-7fe99a87c641")
        };

    /// <inheritdoc />
    public Task<Result<string>> GetPathAsync(
        KnownFolderKind folder,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!FolderIds.TryGetValue(folder, out var folderId))
        {
            return Task.FromResult(Result<string>.Failure("That folder is not supported."));
        }

        var path = Resolve(folderId);
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(Result<string>.Failure($"I couldn't locate your {folder.ToString().ToLowerInvariant()} folder."));
        }

        return Task.FromResult(Result<string>.Success(path));
    }

    private static string? Resolve(Guid folderId)
    {
        var buffer = Marshal.AllocCoTaskMem(BufferSize);
        try
        {
            var result = SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, buffer);
            if (result != 0)
            {
                return null;
            }

            return Marshal.PtrToStringUni(buffer);
        }
        catch (Exception)
        {
            // A known folder that cannot be resolved is reported as a failure by the caller;
            // there is nothing useful to add here and no reason to tear down the process.
            return null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(
        ref Guid folderId,
        uint flags,
        IntPtr token,
        IntPtr buffer);
}
