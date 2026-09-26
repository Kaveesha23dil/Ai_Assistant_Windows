using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Files.Queries.SearchFiles;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.FileSearch;

/// <summary>
/// Handles "find my invoices" by delegating to the existing file search query.
/// <para>
/// Only a small number of hits is spoken. A list of file names read aloud would be unusable,
/// so the full result set travels in the result data for the user interface instead.
/// </para>
/// </summary>
public sealed class FileSearchVoiceHandler : VoiceHandlerBase
{
    private const int MaximumSpokenResults = 3;

    private static readonly AssistantIntent[] Supported = [AssistantIntent.FileSearch];

    private readonly SearchFilesHandler _searchFiles;

    public FileSearchVoiceHandler(
        SearchFilesHandler searchFiles,
        IPermissionService permissions,
        ILogger<FileSearchVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(searchFiles);

        _searchFiles = searchFiles;
    }

    /// <inheritdoc />
    public override IReadOnlyCollection<AssistantIntent> Intents => Supported;

    /// <inheritdoc />
    public override async Task<VoiceCommandResult> ExecuteAsync(
        VoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Permissions.IsGranted(PermissionCapability.FileSearch))
        {
            return Denied(command, PermissionCapability.FileSearch);
        }

        if (!TryGetRequiredParameter(
                command,
                VoiceCommand.QueryParameter,
                out var query,
                out var failure))
        {
            return failure;
        }

        try
        {
            var results = await _searchFiles
                .HandleAsync(new SearchFilesQuery(query), cancellationToken)
                .ConfigureAwait(false);

            if (results.Count == 0)
            {
                return VoiceCommandResult.Success(command, $"I couldn't find anything about {query}.");
            }

            var spoken = results
                .Take(MaximumSpokenResults)
                .Select(result => result.Name)
                .ToArray();

            var more = results.Count - spoken.Length;

            var response = spoken.Length switch
            {
                1 => $"I found one file, {spoken[0]}.",
                _ => $"I found {results.Count} files, starting with {string.Join(", ", spoken)}."
            };

            if (more > 0)
            {
                response += $" {more} more in the results list.";
            }

            return VoiceCommandResult.Success(
                command,
                response,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["query"] = query,
                    ["resultCount"] = results.Count.ToString(CultureInfo.InvariantCulture)
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failed(command, exception);
        }
    }
}
