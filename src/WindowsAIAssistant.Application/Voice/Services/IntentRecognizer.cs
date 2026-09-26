using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Application.Common.Errors;
using WindowsAIAssistant.Application.Voice.Text;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Voice;

namespace WindowsAIAssistant.Application.Voice.Services;

/// <summary>
/// Deterministic, local intent recognition.
/// <para>
/// The whole point is that a spoken "volume up" or "what time is it" never leaves the
/// machine and never waits on a model. The rule table is consulted in priority order and the
/// first match wins; only a sentence that matches nothing reaches the AI question rule, which
/// is deliberately last and carries a lower confidence so a vague match is confirmed rather
/// than executed.
/// </para>
/// </summary>
public sealed class IntentRecognizer : IIntentRecognizer
{
    /// <summary>
    /// A capture group named this way continues the primary query, which keeps patterns such
    /// as "find documents about X" from needing a single greedy group.
    /// </summary>
    private const string ContinuationGroupName = "rest";

    private readonly IReadOnlyList<IntentRule> _rules;
    private readonly ILogger<IntentRecognizer> _logger;

    public IntentRecognizer(ILogger<IntentRecognizer> logger)
        : this(IntentRuleSet.Create(), logger)
    {
    }

    internal IntentRecognizer(IReadOnlyList<IntentRule> rules, ILogger<IntentRecognizer> logger)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(logger);

        _rules = rules;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<AssistantIntent> SupportedIntents =>
        _rules.Select(rule => rule.Intent).Distinct().ToArray();

    /// <inheritdoc />
    public Task<Result<VoiceCommand>> RecognizeAsync(
        string transcript,
        double recognitionConfidence = 1.0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = VoiceTextNormalizer.Normalize(transcript);
        if (normalized.Length == 0)
        {
            return Task.FromResult(Result<VoiceCommand>.Failure(
                ErrorCodes.VoiceCommandNotRecognized));
        }

        var recognitionScore = Math.Clamp(recognitionConfidence, 0.0, 1.0);

        foreach (var rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parameters = rule.Match(normalized);
            if (parameters is null)
            {
                continue;
            }

            MergeContinuation(parameters);
            NormalizeParameters(rule.Intent, parameters);

            var command = VoiceCommand.Create(
                transcript,
                rule.Intent,
                parameters,
                rule.Confidence * recognitionScore,
                rule.SafetyLevel,
                rule.RequiresConfirmation);

            _logger.LogInformation(
                "Voice intent detected: {Intent} with confidence {Confidence:0.00}.",
                command.Intent,
                command.Confidence);

            return Task.FromResult(Result<VoiceCommand>.Success(command));
        }

        _logger.LogInformation("Voice transcript did not match any known intent.");
        return Task.FromResult(Result<VoiceCommand>.Failure(
            ErrorCodes.VoiceCommandNotRecognized));
    }

    /// <summary>
    /// Folds a continuation group into the primary query so a handler receives one usable
    /// string instead of two fragments.
    /// </summary>
    private static void MergeContinuation(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue(ContinuationGroupName, out var continuation))
        {
            return;
        }

        parameters.Remove(ContinuationGroupName);

        if (parameters.TryGetValue(VoiceCommand.QueryParameter, out var primary))
        {
            parameters[VoiceCommand.QueryParameter] = $"{primary} {continuation}";
            return;
        }

        parameters[VoiceCommand.QueryParameter] = continuation;
    }

    /// <summary>
    /// Rewrites matched fragments into the canonical value a handler expects, for example
    /// turning the spoken "fifty percent" into the integer fifty.
    /// </summary>
    private static void NormalizeParameters(AssistantIntent intent, Dictionary<string, string> parameters)
    {
        if (parameters.TryGetValue(VoiceCommand.QueryParameter, out var query))
        {
            parameters[VoiceCommand.QueryParameter] = VoiceTextNormalizer.ToQueryText(query);
        }

        if (parameters.TryGetValue(VoiceCommand.ApplicationParameter, out var application))
        {
            parameters[VoiceCommand.ApplicationParameter] = CleanApplicationName(application);
        }

        if (intent == AssistantIntent.SetVolume
            && parameters.TryGetValue(VoiceCommand.VolumeParameter, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var volume))
        {
            parameters[VoiceCommand.VolumeParameter] = volume.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string CleanApplicationName(string value)
    {
        var cleaned = value.Trim();

        foreach (var suffix in new[] { " for me", " please", " now" })
        {
            if (cleaned.EndsWith(suffix, StringComparison.Ordinal))
            {
                cleaned = cleaned[..^suffix.Length].Trim();
            }
        }

        return VoiceTextNormalizer.ToQueryText(cleaned);
    }
}
