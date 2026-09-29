using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using IOcrProvider = WindowsAIAssistant.Core.Abstractions.Vision.IOcrProvider;
using IOcrProviderResolver = WindowsAIAssistant.Core.Abstractions.Vision.IOcrProviderResolver;

namespace WindowsAIAssistant.Infrastructure.Vision.Ocr;

/// <summary>
/// Chooses which text engine this machine will use, once, and explains itself.
/// <para>
/// The order of preference is a visible list here rather than a chain of type tests spread
/// through the reading path. It is decided by two things, in this order: what the person asked
/// for in settings, and then what the machine can actually do. A named engine that turns out
/// to be unavailable falls back rather than failing, because "read my screen" is worth doing
/// with an older recogniser and is not worth failing over.
/// </para>
/// <para>
/// An engine that would fetch a model is only chosen when the person agreed to that in
/// settings. Consent to look at the screen is not consent to download a hundred megabytes, and
/// a resolver that cannot tell those apart is a resolver that quietly takes the second one.
/// </para>
/// </summary>
public sealed class OcrProviderResolver : IOcrProviderResolver
{
    private readonly IReadOnlyDictionary<OcrProviderKind, IOcrProvider> _providers;
    private readonly IOptionsMonitor<VisionOptions> _options;
    private readonly ILogger<OcrProviderResolver> _logger;

    private readonly Lock _gate = new();

    private OcrProviderKind? _resolved;
    private bool _resolvedRequiresDownload;
    private bool _resolvedWithheldForDownload;

    public OcrProviderResolver(
        IEnumerable<IOcrProvider> providers,
        IOptionsMonitor<VisionOptions> options,
        ILogger<OcrProviderResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;

        // Last registration wins for a given engine, so a host can replace one without having to
        // remove another, and a machine with two engines registered never ends up with a
        // dictionary whose keys are not unique.
        var table = new Dictionary<OcrProviderKind, IOcrProvider>();

        foreach (var provider in providers)
        {
            if (provider is not null)
            {
                table[provider.Kind] = provider;
            }
        }

        _providers = table;
    }

    /// <inheritdoc />
    public OcrProviderKind Resolve()
    {
        if (_resolved is { } cached)
        {
            return cached;
        }

        lock (_gate)
        {
            if (_resolved is { } again)
            {
                return again;
            }

            var (kind, requiresDownload, withheld) = Choose();
            _resolvedRequiresDownload = requiresDownload;
            _resolvedWithheldForDownload = withheld;
            _resolved = kind;

            _logger.LogInformation(
                "Text recognition resolved to {Provider} on this machine.",
                kind == OcrProviderKind.None ? "no engine" : kind.ToString());

            return kind;
        }
    }

    /// <inheritdoc />
    public bool RequiresModelDownload
    {
        get
        {
            Resolve();
            return _resolvedRequiresDownload;
        }
    }

    /// <inheritdoc />
    public Result<string> DescribeAvailability()
    {
        var kind = Resolve();

        if (kind == OcrProviderKind.None)
        {
            // Two different sentences, because they are two different problems. "This computer
            // cannot read text" sends somebody looking for hardware that is not the issue, while
            // "allow the download" is a switch they can turn and a task that then succeeds.
            //
            // The test is not whether the engine downloads anything, but whether an engine was
            // held back for that reason. An engine that was selected because downloading was
            // allowed is working, and reporting it as a refusal would put a "permission denied"
            // in front of a person whose text recognition is about to succeed.
            if (_resolvedWithheldForDownload)
            {
                return Result<string>.Failure(
                    ErrorCodes.OcrPermissionDenied,
                    "Reading text would need to download a recognition model first. Allow that in "
                    + "Settings, or leave it off and I will describe the screen without reading it.");
            }

            return Result<string>.Failure(
                ErrorCodes.OcrUnavailable,
                "This computer has no text recognition available, so I can't read words out of "
                + "a screenshot. I can still describe what is on the screen.");
        }

        return Result<string>.Success(kind == OcrProviderKind.WindowsAi
            ? "Reading text from screenshots with the Windows text recogniser."
            : "Reading text from screenshots with the built-in Windows recogniser.");
    }

    /// <summary>
    /// Applies the configured preference, then this machine's capabilities, in that order.
    /// </summary>
    /// <returns>
    /// The chosen engine, whether that engine would fetch a model on first use, and whether an
    /// engine that was present and usable was passed over purely because fetching one would
    /// have needed permission. The last two are reported apart because they mean opposite
    /// things to a person reading the status line: one is a fact about an engine now in use, the
    /// other is an offer of a choice somebody has not made yet.
    /// </returns>
    private (OcrProviderKind Kind, bool RequiresDownload, bool Withheld) Choose()
    {
        var allowDownload = _options.CurrentValue.AllowOcrModelDownload;
        var preference = _options.CurrentValue.PreferredOcrProvider ?? "Auto";

        // Set when an engine was present, usable, and was passed over only because it would have
        // fetched something. It is the difference between "this computer cannot read text" and
        // "this computer can, if you let it download something first".
        var skipped = false;

        // Newest first, because a better reading is a better answer and the older engine is
        // the fallback rather than the destination.
        var order = new[] { OcrProviderKind.WindowsAi, OcrProviderKind.WindowsLegacy };

        var named = TryParse(preference);
        if (named is { } wanted && wanted != OcrProviderKind.None)
        {
            if (TryAccept(wanted, allowDownload, ref skipped) is { } chosen)
            {
                return (chosen.Kind, chosen.RequiresDownload, Withheld: false);
            }

            _logger.LogInformation(
                "The configured text engine {Engine} is not usable here, so a different one "
                + "will be used instead.",
                wanted);
        }

        foreach (var candidate in order)
        {
            if (TryAccept(candidate, allowDownload, ref skipped) is { } accepted)
            {
                // An engine was passed over for wanting a download, but a different one was
                // chosen and works now, so there is nothing to ask the person about.
                return (accepted.Kind, accepted.RequiresDownload, Withheld: false);
            }
        }

        // Nothing was chosen, so "would have downloaded" describes the engines that were held
        // back rather than an engine in use, and the status line needs to offer the choice.
        return (OcrProviderKind.None, RequiresDownload: false, Withheld: skipped);

        (OcrProviderKind Kind, bool RequiresDownload)? TryAccept(
            OcrProviderKind candidate,
            bool downloadsAllowed,
            ref bool sawEngineNeedingDownload)
        {
            if (!_providers.TryGetValue(candidate, out var provider) || !provider.IsAvailable)
            {
                return null;
            }

            if (provider.RequiresModelDownload && !downloadsAllowed)
            {
                // Not chosen, and not chosen quietly: the flag below is what turns this into a
                // sentence in the status area offering the person the choice.
                sawEngineNeedingDownload = true;
                return null;
            }

            return (candidate, provider.RequiresModelDownload);
        }
    }

    /// <summary>
    /// Reads the configured engine name, tolerating the way a person would type it.
    /// </summary>
    private static OcrProviderKind? TryParse(string preference) => preference.Trim() switch
    {
        "WindowsAi" or "WindowsAI" or "Ai" or "AI" => OcrProviderKind.WindowsAi,
        "WindowsLegacy" or "Legacy" => OcrProviderKind.WindowsLegacy,
        "None" => OcrProviderKind.None,
        _ => null,
    };
}
