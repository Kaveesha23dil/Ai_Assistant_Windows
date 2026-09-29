using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Knowledge.Queries.AskKnowledgeQuestion;
using WindowsAIAssistant.Application.Voice.Commands;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Commands.Knowledge;

/// <summary>
/// Answers a spoken question from the person's own indexed documents.
/// <para>
/// The separate handler that the general AI question handler is not. "What does my contract say
/// about notice" and "what is the capital of France" are both questions, and sending the first to
/// a model with no documents attached produces a confident answer from its memory of contracts
/// in general. Asking the index is the difference between an answer somebody can check and one
/// they cannot, and the fact that this intent exists at all is what makes that choice possible at
/// the moment somebody says the words.
/// </para>
/// <para>
/// Nothing here decides what may leave the machine. The retriever, the context builder, and the
/// answer service each ask the consent policy themselves, and the permission is checked here only
/// to give a spoken refusal before any work starts. Deciding it twice, in two places that could
/// disagree, is the failure mode the rest of the voice layer is built to avoid.
/// </para>
/// </summary>
public sealed class KnowledgeVoiceHandler : VoiceHandlerBase
{
    private const int MaximumSpokenCharacters = 400;

    private static readonly AssistantIntent[] Supported = [AssistantIntent.KnowledgeQuestion];

    /// <summary>
    /// Built once from the phrase list. A whole-word match, with a negative lookbehind so that a
    /// phrase starting part-way through a longer word is not a match either — the regex engine's
    /// <c>\b</c> would stop "on" from matching inside "information", and the lookbehind stops
    /// "for" from matching the tail of "therefor".
    /// </summary>
    private readonly AskKnowledgeQuestionHandler _ask;
    private readonly VoiceIntentPolicy _policy;

    public KnowledgeVoiceHandler(
        AskKnowledgeQuestionHandler ask,
        VoiceIntentPolicy policy,
        IPermissionService permissions,
        ILogger<KnowledgeVoiceHandler> logger)
        : base(permissions, logger)
    {
        ArgumentNullException.ThrowIfNull(ask);
        ArgumentNullException.ThrowIfNull(policy);

        _ask = ask;
        _policy = policy;
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

        if (!Permissions.IsGranted(PermissionCapability.KnowledgeBase))
        {
            return Denied(command, PermissionCapability.KnowledgeBase);
        }

        if (!TryGetRequiredParameter(
                command,
                VoiceCommand.QueryParameter,
                out var question,
                out var failure))
        {
            return failure;
        }

        // The spoken "my documents" and the "about my documents" a rule might capture are both
        // describing the source rather than the question, and a question that still contains
        // them would search for the word "documents" in every document.
        var cleaned = StripSourceWords(question);

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return Invalid(command, "I didn't catch what to look for.");
        }

        try
        {
            var answer = await _ask
                .HandleAsync(cleaned, resultCount: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (Refused(command, answer) is { } refusal)
            {
                return refusal;
            }

            if (!answer.IsSuccess)
            {
                return Unavailable(command, answer.ErrorMessage ?? "I couldn't search your documents.");
            }

            if (answer.Sources.Count == 0)
            {
                return VoiceCommandResult.Success(
                    command,
                    "I couldn't find anything about that in your documents.",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["answered"] = "false",
                        ["retrieved"] = "0",
                    });
            }

            var spoken = Speakable(answer.Answer, _policy.MaximumSpokenResponseLength);
            var first = answer.Sources[0];

            // The source is named aloud, and the fuller list is left on the page. A citation
            // read out in full — file name, page, section — is longer than the answer it is
            // supporting, and the file's name is the part a person needs to hear to be able to
            // go and check it.
            return VoiceCommandResult.Success(
                command,
                $"{spoken} That's from {first.FileName}.",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["answered"] = "true",
                    ["retrieved"] = answer.RetrievedCount.ToString(CultureInfo.InvariantCulture),
                    ["sources"] = string.Join(", ", answer.Sources.Select(source => source.Citation())),
                    ["keywordOnly"] = answer.IsKeywordOnly ? "true" : "false",
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
    /// Turns a consent refusal into a permission failure, or returns nothing when the result is
    /// not one.
    /// <para>
    /// The decision is not made here for the same reason the document path does not make it:
    /// whether passages may be sent anywhere is settled by the policy inside the retriever and
    /// the answer service, which know which provider is in use. Reaching the same conclusion
    /// from the same settings twice is how the voice path and the page end up disagreeing.
    /// </para>
    /// </summary>
    private VoiceCommandResult? Refused(VoiceCommand command, KnowledgeAnswer answer)
    {
        if (answer.ErrorCode is not (
            ErrorCodes.KnowledgeAiPermissionDenied
            or ErrorCodes.KnowledgeEmbeddingPermissionDenied))
        {
            return null;
        }

        Logger.LogInformation(
            "Voice action {Intent} was refused: the knowledge consent policy declined it.",
            command.Intent);

        return VoiceCommandResult.Failure(
            command,
            answer.ErrorCode,
            answer.ErrorMessage ?? Permissions.GetDeniedMessage(PermissionCapability.KnowledgeBase),
            isPermissionDenied: true);
    }

    /// <summary>
    /// Removes the words that named the source, so the question that reaches the index is the
    /// question rather than the sentence that asked it.
    /// <para>
    /// Whole words only, and this matters more than it looks. These phrases include "for" and
    /// "on", and a plain substring replacement deletes the "on" at the end of "information" and
    /// the "for" inside "before" — so a question about information would arrive at the index as
    /// "what does my contract say about infrmati", find nothing, and report that the document did
    /// not mention it. The failure is silent and looks exactly like a document that does not
    /// contain the answer, which is the sort of wrong that erodes somebody's trust in the index
    /// for good. A regex with word boundaries only removes a phrase that is genuinely a word of
    /// its own.
    /// </para>
    /// <para>
    /// The phrase list is tried longest first, so "in my knowledge base" is removed as one phrase
    /// rather than as "in my" followed by whatever the next entry happened to match.
    /// </para>
    /// </summary>
    private static string StripSourceWords(string question)
    {
        var cleaned = question;

        foreach (var phrase in SourcePhrases)
        {
            cleaned = Phrase.Replace(cleaned, " ");
        }

        // Speech recognition leaves the pause before a question as a comma, so removing "from the
        // knowledge base" from "from the knowledge base, what is the fee" leaves ", what is the
        // fee". A leading comma is a token the index would carry through every score it computes
        // and match nothing, so the edges are trimmed rather than left to be found later.
        return string.Join(' ', cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim([',', '.', ';', ':', '?', '!', '-', '"', '\'']))
            .Where(token => token.Length > 0)
            .Select(token => token.ToLowerInvariant()));
    }

    /// <summary>
    /// The words that name where the answer should come from, and the framing words a rule may
    /// have captured along with them. Ordered longest first, which is the order they are tried in.
    /// </summary>
    private static readonly string[] SourcePhrases =
    [
        "in my knowledge base", "in the knowledge base",
        "from my knowledge base", "from the knowledge base",
        "in my documents", "in the documents",
        "from my documents", "from the documents",
        "in my notes", "in the notes",
        "in my files", "in the files",
        "from my notes", "from the notes",
        "from my files", "from the files",
        "my knowledge base", "the knowledge base",
        "my documents", "the documents",
        "my notes", "the notes",
        "my files", "the files",
        "say about", "about", "regarding", "for", "on",
    ];

    /// <summary>
    /// Built once from the phrase list. A whole-word match, with a negative lookbehind so that a
    /// phrase starting part-way through a longer word is not a match either — the regex engine's
    /// <c>\b</c> would stop "on" from matching inside "information", and the lookbehind stops
    /// "for" from matching the tail of "therefor".
    /// <para>
    /// Declared after the list because a static field is initialised in the order it is written,
    /// and this one is built from it.
    /// </para>
    /// </summary>
    private static readonly Regex Phrase = new(
        @"(?<!\w)(?:" + string.Join('|', SourcePhrases.Select(Regex.Escape)) + @")(?!\w)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Trims an answer to something worth listening to, cutting at a sentence or a space rather
    /// than mid-word, and saying that it was cut.
    /// </summary>
    private static string Speakable(string text, int maximumCharacters)
    {
        var budget = Math.Min(maximumCharacters, MaximumSpokenCharacters);
        var trimmed = text.Trim();

        if (trimmed.Length <= budget)
        {
            return trimmed;
        }

        var cut = trimmed[..budget];
        var lastStop = cut.LastIndexOfAny(['.', '!', '?']);
        if (lastStop > budget / 2)
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

        return $"{cut.TrimEnd()} The full answer is on the knowledge page.";
    }
}
