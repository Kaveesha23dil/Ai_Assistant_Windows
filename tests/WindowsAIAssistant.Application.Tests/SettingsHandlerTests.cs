using WindowsAIAssistant.Application.Settings.Commands.UpdateSetting;
using WindowsAIAssistant.Application.Settings.Queries.GetSetting;
using WindowsAIAssistant.Application.Tests.Fakes;

namespace WindowsAIAssistant.Application.Tests;

public sealed class SettingsHandlerTests
{
    [Fact]
    public async Task UpdateThenGetSetting_StoresAndRetrievesValue()
    {
        var storage = new FakeSettingsStorage();
        var updateHandler = new UpdateSettingHandler<string>(storage);
        var getHandler = new GetSettingHandler<string>(storage);

        var updateResult = await updateHandler.HandleAsync(new UpdateSettingCommand<string>("Theme", "Dark"));
        var value = await getHandler.HandleAsync(new GetSettingQuery<string>("Theme"));

        Assert.True(updateResult.IsSuccess);
        Assert.Equal("Dark", value);
    }

    [Fact]
    public async Task GetSetting_WithEmptyKey_ThrowsArgumentException()
    {
        var handler = new GetSettingHandler<string>(new FakeSettingsStorage());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(new GetSettingQuery<string>(" ")));
    }

    [Fact]
    public async Task UpdateSetting_WithEmptyKey_ThrowsArgumentException()
    {
        var handler = new UpdateSettingHandler<string>(new FakeSettingsStorage());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(
            new UpdateSettingCommand<string>("", "Dark")));
    }
}
