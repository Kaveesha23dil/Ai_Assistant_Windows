using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Documents.Queries.AskDocumentQuestion;
using WindowsAIAssistant.Application.Documents.Queries.SummarizeDocument;
using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Abstractions.Navigation;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Documents;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.Document;

/// <summary>
/// Handles spoken requests about a document: opening it, summarizing it, and asking it a
/// question.
/// <para>
/// Opening a document is treated as harmless and needs no consent, because it moves the
/// interface to a page and reads nothing. Summarizing and asking are not harmless in that
/// sense: both read the file and both may send its text to a provider, so both ask the
/// document permission first, and a provider running on this machine is left to the consent
/// policy beneath them rather than being second-guessed here.
/// </para>
/// <para>
/// What is spoken is deliberately short. A summary of a long document read aloud is a
/// paragraph nobody can act on, so the full result is left in the document page and the
/// spoken answer says what was done and where to read it. A short question gets its answer
/// read out, capped, because an answer that runs to a page is worse than one that is cut off.
/// </para>
/// </summary>
public sealed class DocumentVoiceHandler : VoiceHandlerBase
{
    private const int MaximumSpokenCharacters = 400;

    private static readonly AssistantIntent[] Supported =
    [
        AssistantIntent.OpenDocument,
        AssistantIntent.SummarizeDocument,
        AssistantIntent.AskDocumentQuestion,
    ];

    private readonly IApplicationNavigator _navigator;
    private readonly SummarizeDocumentHandler _summarize;
    private readonly AskDocumentQuestionHandler _ask;

    public DocumentVoiceHandler(
        IApplicationNavigator navigator,
        SummarizeDocumentHandler summarize,
        AskDocumentQuestionHandler ask,
        IPermissionService permissions,
        ILogger<DocumentVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(summarize);
        ArgumentNullException.ThrowIfNull(ask);

        _navigator = navigator;
        _summarize = summarize;
        _ask = ask;
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

        if (!TryGetRequiredParameter(
                command,
                VoiceCommand.FileParameter,
                out var file,
                out var failure))
        {
            return failure;
        }

        return command.Intent switch
        {
            AssistantIntent.OpenDocument => Open(command, file),
            AssistantIntent.SummarizeDocument => await SummarizeAsync(command, file, cancellationToken)
                .ConfigureAwait(false),
            AssistantIntent.AskDocumentQuestion => await AskAsync(command, file, cancellationToken)
                .ConfigureAwait(false),
            _ => Invalid(command, "I didn't understand that document request."),
        };
    }

    private VoiceCommandResult Open(VoiceCommand command, string file)
    {
        var name = Path.GetFileName(file);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Invalid(command, "I didn't catch which file you meant.");
        }

        try
        {
            return _navigator.Navigate(NavigationRoute.Document, file)
                ? VoiceCommandResult.Success(command, $"Opening {name}.")
                : Unavailable(command, $"I couldn't open {name}.");
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

    private async Task<VoiceCommandResult> SummarizeAsync(
        VoiceCommand command,
        string file,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _summarize
                .HandleAsync(new SummarizeDocumentCommand(file), cancellationToken)
                .ConfigureAwait(false);

            return Refused(command, dto)
                ?? Describe(command, dto.FileName, dto.ErrorMessage, dto.IsSuccess, dto.Warnings,
                    "I've summarized it");
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

    private async Task<VoiceCommandResult> AskAsync(
        VoiceCommand command,
        string file,
        CancellationToken cancellationToken)
    {
        if (!TryGetRequiredParameter(
                command,
                VoiceCommand.QueryParameter,
                out var question,
                out var failure))
        {
            return failure;
        }

        try
        {
            var dto = await _ask
                .HandleAsync(new AskDocumentQuestionQuery(file, question), cancellationToken)
                .ConfigureAwait(false);

            if (Refused(command, dto) is { } refused)
            {
                return refused;
            }

            if (!dto.IsSuccess)
            {
                return Unavailable(command, dto.ErrorMessage ?? "I couldn't read that document.");
            }

            if (dto.IsNotInDocument)
            {
                return VoiceCommandResult.Success(
                    command,
                    $"{dto.FileName} doesn't say anything about that.",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["file"] = dto.FileName,
                        ["answered"] = "false",
                    });
            }

            return VoiceCommandResult.Success(
                command,
                Speakable(dto.Text),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["file"] = dto.FileName,
                    ["answered"] = "true",
                    ["references"] = string.Join(", ", dto.References),
                    ["characters"] = dto.Text.Length.ToString(CultureInfo.InvariantCulture),
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

    /// <summary>
    /// Turns a refusal into a permission failure, or returns nothing when the result is not one.
    /// <para>
    /// The decision is not made here. Whether a document's text may be sent anywhere is settled
    /// once, by the consent policy inside the analysis service, which knows whether the provider
    /// in use is on this machine. Asking the question again in the voice path could only reach
    /// the opposite conclusion from the same settings, which is the sort of disagreement that
    /// shows up as voice working and the page not.
    /// </para>
    /// </summary>
    private VoiceCommandResult? Refused(VoiceCommand command, DocumentAnalysisDto dto)
    {
        if (!string.Equals(dto.ErrorCode, ErrorCodes.DocumentAiPermissionDenied, StringComparison.Ordinal))
        {
            return null;
        }

        Logger.LogInformation(
            "Voice action {Intent} was refused: the document consent policy declined it.",
            command.Intent);

        return VoiceCommandResult.Failure(
            command,
            dto.ErrorCode!,
            dto.ErrorMessage ?? Permissions.GetDeniedMessage(PermissionCapability.DocumentCloudProcessing),
            isPermissionDenied: true);
    }

    private VoiceCommandResult Describe(
        VoiceCommand command,
        string fileName,
        string? error,
        bool isSuccess,
        IReadOnlyCollection<DocumentWarningDto> warnings,
        string pastTense)
    {
        if (!isSuccess)
        {
            return Unavailable(command, error ?? "I couldn't read that document.");
        }

        var response = $"{pastTense} {fileName}. The full summary is on the document page.";

        // A limitation met while reading is spoken as well, because a summary of a document
        // with a scanned appendix is not the same as a summary of the document.
        var firstWarning = warnings.FirstOrDefault();
        if (firstWarning is not null)
        {
            response += $" One thing to know: {firstWarning.Message}";
        }

        return VoiceCommandResult.Success(
            command,
            response,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["file"] = fileName,
                ["warnings"] = warnings.Count.ToString(CultureInfo.InvariantCulture),
            });
    }

    /// <summary>
    /// Trims an answer to something worth listening to, cutting at a sentence or a space rather
    /// than mid-word, and saying that it was cut.
    /// </summary>
    private static string Speakable(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.Length <= MaximumSpokenCharacters)
        {
            return trimmed;
        }

        var cut = trimmed[..MaximumSpokenCharacters];
        var lastStop = cut.LastIndexOfAny(['.', '!', '?']);
        if (lastStop > MaximumSpokenCharacters / 2)
        {
            cut = cut[..(lastStop + 1)];
        }
        else
        {
            var lastSpace = cut.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                cut = cut[..lastSpace];
            }
        }

        return $"{cut.TrimEnd()} The full answer is on the document page.";
    }
}
