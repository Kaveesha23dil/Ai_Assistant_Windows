namespace WindowsAIAssistant.Application.Settings.Commands.UpdateSetting;

public sealed record UpdateSettingCommand<T>(string Key, T Value);
