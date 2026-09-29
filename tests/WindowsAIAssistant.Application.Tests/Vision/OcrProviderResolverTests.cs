using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Application.Tests.Helpers;
using WindowsAIAssistant.Core.Abstractions.Vision;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Configuration.Options;
using WindowsAIAssistant.Infrastructure.Vision.Ocr;

namespace WindowsAIAssistant.Application.Tests.Vision;

/// <summary>
/// Covers which text engine gets chosen, and what the status line says about it.
/// <para>
/// This is the narrowest privacy decision in the feature and the easiest one to get backwards.
/// Reading a screenshot and fetching a hundred megabytes of model are separate acts, and a
/// resolver that treats them as one will either download without asking or refuse a machine
/// that could have answered. The tests below pin both directions: nothing is fetched without
/// permission, and a working engine is never reported as a refusal.
/// </para>
/// <para>
/// The failure message matters as much as the choice. A person whose only barrier is a setting
/// they have not found yet cannot act on "this computer cannot read text", and a person whose
/// engine works is entitled to know that rather than being shown a permission error.
/// </para>
/// </summary>
public sealed class OcrProviderResolverTests
{
    [Fact]
    public void TheNewestEngineIsPreferredOverTheOlderOne()
    {
        var ai = new FakeOcrProvider(OcrProviderKind.WindowsAi, isAvailable: true, requiresModelDownload: true);
        var legacy = new FakeOcrProvider(OcrProviderKind.WindowsLegacy, isAvailable: true, requiresModelDownload: false);

        // With the download permitted, the newer engine wins: a better reading is a better answer,
        // and the older recogniser is the fallback rather than the destination.
        var resolver = Resolver([ai, legacy], allowOcrModelDownload: true);

        Assert.Equal(OcrProviderKind.WindowsAi, resolver.Resolve());
    }

    [Fact]
    public void AnEngineThatWouldFetchAModelIsNotChosenWithoutPermission()
    {
        var ai = new FakeOcrProvider(OcrProviderKind.WindowsAi, isAvailable: true, requiresModelDownload: true);
        var legacy = new FakeOcrProvider(OcrProviderKind.WindowsLegacy, isAvailable: true, requiresModelDownload: false);

        // The model is available and the machine can use it. It is still not chosen, because
        // nobody agreed to the download that using it would start.
        var resolver = Resolver([ai, legacy], allowOcrModelDownload: false);

        Assert.Equal(OcrProviderKind.WindowsLegacy, resolver.Resolve());
        Assert.Equal(0, ai.ReadCount);
    }

    [Fact]
    public void AnUnusableEngineIsSkippedRatherThanFailingTheRequest()
    {
        var ai = new FakeOcrProvider(OcrProviderKind.WindowsAi, isAvailable: false, requiresModelDownload: true);
        var legacy = new FakeOcrProvider(OcrProviderKind.WindowsLegacy, isAvailable: true, requiresModelDownload: false);

        var resolver = Resolver([ai, legacy]);

        // "Read my screen" is worth doing with an older recogniser and is not worth failing over.
        Assert.Equal(OcrProviderKind.WindowsLegacy, resolver.Resolve());
    }

    [Fact]
    public void ANamedEngineThatCannotBeUsedFallsBackInsteadOfFailing()
    {
        var ai = new FakeOcrProvider(OcrProviderKind.WindowsAi, isAvailable: false, requiresModelDownload: true);
        var legacy = new FakeOcrProvider(OcrProviderKind.WindowsLegacy, isAvailable: true, requiresModelDownload: false);

        var resolver = Resolver([ai, legacy], preferred: "WindowsAi");

        Assert.Equal(OcrProviderKind.WindowsLegacy, resolver.Resolve());
    }

    [Fact]
    public void ANamedEngineIsHonouredWhenItCanBeUsed()
    {
        var ai = new FakeOcrProvider(OcrProviderKind.WindowsAi, isAvailable: true, requiresModelDownload: true);
        var legacy = new FakeOcrProvider(OcrProviderKind.WindowsLegacy, isAvailable: true, requiresModelDownload: false);

        var resolver = Resolver([ai, legacy], preferred: "WindowsLegacy", allowOcrModelDownload: true);

        Assert.Equal(OcrProviderKind.WindowsLegacy, resolver.Resolve());
    }

    [Fact]
    public void WhenTheOnlyUsableEngineNeedsADownloadTheStatusOffersTheChoice()
    {
        var ai = new FakeOcrProvider(OcrProviderKind.WindowsAi, isAvailable: true, requiresModelDownload: true);

        var resolver = Resolver([ai], allowOcrModelDownload: false);

        var described = resolver.DescribeAvailability();

        // Nothing is selected, but nothing is impossible either. The sentence has to point at
        // the switch, because that is the thing the person can actually do about it.
        Assert.Equal(OcrProviderKind.None, resolver.Resolve());
        Assert.True(described.IsFailure);
        Assert.Equal(ErrorCodes.OcrPermissionDenied, described.ErrorCode);
        Assert.Contains("download", described.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WhenNoEngineExistsAtAllTheStatusSaysSoRatherThanOfferingADownload()
    {
        var ai = new FakeOcrProvider(OcrProviderKind.WindowsAi, isAvailable: false, requiresModelDownload: true);
        var legacy = new FakeOcrProvider(OcrProviderKind.WindowsLegacy, isAvailable: false, requiresModelDownload: false);

        var resolver = Resolver([ai, legacy], allowOcrModelDownload: true);

        var described = resolver.DescribeAvailability();

        // Offering a download here would send somebody to a setting that cannot help them. The
        // machine has no recogniser to download into.
        Assert.Equal(OcrProviderKind.None, resolver.Resolve());
        Assert.True(described.IsFailure);
        Assert.Equal(ErrorCodes.OcrUnavailable, described.ErrorCode);
    }

    [Fact]
    public void AWorkingEngineThatDownloadsItsModelIsNotReportedAsARefusal()
    {
        var ai = new FakeOcrProvider(OcrProviderKind.WindowsAi, isAvailable: true, requiresModelDownload: true);

        // Consent was given, so the engine is in use and reading is about to work.
        var resolver = Resolver([ai], allowOcrModelDownload: true);

        var described = resolver.DescribeAvailability();

        Assert.Equal(OcrProviderKind.WindowsAi, resolver.Resolve());
        Assert.True(described.IsSuccess);
        Assert.True(resolver.RequiresModelDownload);
    }

    [Fact]
    public void AnEngineThatNeverDownloadsDoesNotClaimTo()
    {
        var legacy = new FakeOcrProvider(OcrProviderKind.WindowsLegacy, isAvailable: true, requiresModelDownload: false);

        var resolver = Resolver([legacy]);

        Assert.Equal(OcrProviderKind.WindowsLegacy, resolver.Resolve());
        Assert.False(resolver.RequiresModelDownload);
    }

    [Fact]
    public void APreferenceIsResolvedOnceSoAProbeIsNotRepeatedForEveryCapture()
    {
        var ai = new FakeOcrProvider(OcrProviderKind.WindowsAi, isAvailable: true, requiresModelDownload: true);

        var resolver = Resolver([ai], allowOcrModelDownload: true);

        Assert.Equal(resolver.Resolve(), resolver.Resolve());
    }

    private static OcrProviderResolver Resolver(
        IEnumerable<IOcrProvider> providers,
        bool allowOcrModelDownload = false,
        string preferred = "Auto") =>
        new(
            providers,
            new TestOptionsMonitor<VisionOptions>(new VisionOptions
            {
                AllowOcrModelDownload = allowOcrModelDownload,
                PreferredOcrProvider = preferred,
            }),
            NullLogger<OcrProviderResolver>.Instance);
}
