using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Windows;

namespace WindowsAIAssistant.Application.Tests.Voice;

/// <summary>
/// The resolver is the boundary that stands between a spoken name and process execution, so
/// these tests care less about which programs exist than about what the resolver refuses.
/// <para>
/// Only entries that resolve without touching the file system or the registry are asserted.
/// A test that depended on a particular program being installed would pass on one machine and
/// fail on another, which is exactly the kind of test that gets deleted and then never
/// replaced.
/// </para>
/// </summary>
public sealed class ApplicationResolverTests
{
    private static ApplicationResolver Create() => new(NullLogger<ApplicationResolver>.Instance);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyNameIsRefusedWithoutAskingTheMachine(string name)
    {
        var resolver = Create();

        var result = await resolver.ResolveAsync(name);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task APackagedApplicationResolvesToItsUserModelId()
    {
        var resolver = Create();

        var result = await resolver.ResolveAsync("calculator");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(ApplicationTargetKind.AppUserModelId, result.Value!.Kind);
    }

    [Fact]
    public async Task AnAliasResolvesToTheSameApplicationAsItsRealName()
    {
        var resolver = Create();

        var result = await resolver.ResolveAsync("calc");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("Calculator", result.Value!.Name);
    }

    [Fact]
    public async Task ASettingsApplicationResolvesToAnAddress()
    {
        var resolver = Create();

        var result = await resolver.ResolveAsync("settings app");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(ApplicationTargetKind.Uri, result.Value!.Kind);
        Assert.Equal("ms-settings:home", result.Value.Target);
    }

    [Fact]
    public async Task AnUnknownNameIsRefusedRatherThanGuessedAt()
    {
        var resolver = Create();

        var result = await resolver.ResolveAsync("qqzzxx-not-an-application");

        // Nothing matched, so nothing is launched. This is the case that matters: an assistant
        // that invents a target for an unrecognized word would eventually run something.
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("cmd.exe /c del everything")]
    [InlineData("notepad & calc")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("../../windows/system32/cmd.exe")]
    public async Task ANameShapedLikeACommandIsRefused(string spoken)
    {
        var resolver = Create();

        var result = await resolver.ResolveAsync(spoken);

        // Nothing concatenates user input into a command line, so a spoken name that looks
        // like one is simply a name that matches nothing.
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ResolutionCanBeAbandoned()
    {
        var resolver = Create();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.ResolveAsync("calculator", cancellation.Token));
    }
}
