using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.Clipboard.Commands.SetClipboardText;
using WindowsAIAssistant.Application.Clipboard.Queries.GetClipboardText;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Application.Tests;

public sealed class ClipboardHandlerTests
{
    [Fact]
    public async Task GetTextAsync_WithClipboardText_ReturnsText()
    {
        var service = new FakeClipboardService { Text = "clipboard value" };
        var handler = new GetClipboardTextHandler(
            service,
            NullLogger<GetClipboardTextHandler>.Instance);

        var result = await handler.HandleAsync(new GetClipboardTextQuery());

        Assert.Equal("clipboard value", result);
        Assert.Equal(1, service.GetCallCount);
    }

    [Fact]
    public async Task GetTextAsync_WithoutClipboardText_ReturnsNull()
    {
        var handler = new GetClipboardTextHandler(
            new FakeClipboardService(),
            NullLogger<GetClipboardTextHandler>.Instance);

        var result = await handler.HandleAsync(new GetClipboardTextQuery());

        Assert.Null(result);
    }

    [Fact]
    public async Task SetTextAsync_WithText_CallsClipboardServiceAndReturnsResult()
    {
        var expectedResult = Result.Success();
        var service = new FakeClipboardService { SetTextResult = expectedResult };
        var handler = new SetClipboardTextHandler(
            service,
            NullLogger<SetClipboardTextHandler>.Instance);

        var result = await handler.HandleAsync(new SetClipboardTextCommand("new value"));

        Assert.Same(expectedResult, result);
        Assert.Equal("new value", service.LastSetText);
        Assert.Equal(1, service.SetCallCount);
    }

    [Fact]
    public async Task SetTextAsync_WithNullText_ThrowsArgumentNullException()
    {
        var service = new FakeClipboardService();
        var handler = new SetClipboardTextHandler(
            service,
            NullLogger<SetClipboardTextHandler>.Instance);

        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.HandleAsync(new SetClipboardTextCommand(null!)));

        Assert.Equal(0, service.SetCallCount);
    }
}
