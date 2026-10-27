using System.Runtime.InteropServices.WindowsRuntime;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Common;

#if WINDOWS
using Windows.ApplicationModel.DataTransfer;
#endif

namespace WindowsAIAssistant.Infrastructure.Windows;

/// <summary>
/// Reads and writes clipboard text through the Windows clipboard.
/// <para>
/// Only text is exchanged. Nothing here logs the clipboard contents, because the clipboard is
/// data the user never intended to publish and may hold a password at the moment it is read.
/// </para>
/// </summary>
public sealed class WindowsClipboardService : IClipboardService
{
    /// <inheritdoc />
    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

#if WINDOWS
        try
        {
            var content = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (content is null || !content.Contains(StandardDataFormats.Text))
            {
                return null;
            }

            return await content
                .GetTextAsync()
                .AsTask()
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The clipboard is frequently locked by another process. Reporting "no text" is the
            // honest answer and keeps a transient lock from surfacing as an error.
            return null;
        }
#else
        await Task.CompletedTask.ConfigureAwait(false);
        return null;
#endif
    }

    /// <inheritdoc />
    public async Task<Result> SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(text))
        {
            return Result.Failure("There was no text to copy.");
        }

#if WINDOWS
        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);

            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            try
            {
                global::Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            }
            catch { }

            await Task.CompletedTask.ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception)
        {
            return Result.Failure("I couldn't write to the clipboard.");
        }
#else
        await Task.CompletedTask.ConfigureAwait(false);
        return Result.Success();
#endif
    }
}
