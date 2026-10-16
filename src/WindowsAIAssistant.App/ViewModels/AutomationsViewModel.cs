using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsAIAssistant.Core.Abstractions.Automations;
using WindowsAIAssistant.Core.Models.Automations;

namespace WindowsAIAssistant.App.ViewModels;

public sealed partial class AutomationsViewModel : ObservableObject
{
    private readonly IAutomationService _service;
    public AutomationsViewModel(IAutomationService service) => _service = service;
    public ObservableCollection<Automation> Items { get; } = [];
    public string[] ActionChoices { get; } = ["Reminder", "Summarize a folder"];
    public string[] RepeatChoices { get; } = ["Once", "Daily", "Weekly (on the selected date's weekday)"];
    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string Details { get; set; } = string.Empty;
    [ObservableProperty] public partial int ActionIndex { get; set; }
    [ObservableProperty] public partial int RepeatIndex { get; set; }
    [ObservableProperty] public partial DateTimeOffset StartDate { get; set; } = DateTimeOffset.Now.AddHours(1).Date;
    [ObservableProperty] public partial TimeSpan StartTime { get; set; } = DateTime.Now.AddHours(1).TimeOfDay;
    [ObservableProperty] public partial Automation? Selected { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public async Task RefreshAsync()
    {
        try
        {
            var items = await _service.ListAsync();
            if (Items.SequenceEqual(items.OrderBy(x => x.NextRunUtc))) return;
            var selectedId = Selected?.Id;
            Items.Clear();
            foreach (var item in items.OrderBy(x => x.NextRunUtc)) Items.Add(item);
            Selected = Items.FirstOrDefault(x => x.Id == selectedId);
        }
        catch (Exception) { Message = "Could not load automations. Check access to the local automations file."; }
    }

    [RelayCommand]
    private async Task CreateAutomationAsync()
    {
        await PerformAsync(async () =>
        {
            await _service.SaveAsync(new Automation
            {
                Title = Title.Trim(), Details = Details.Trim(), Kind = (AutomationKind)ActionIndex,
                Repeat = (AutomationRepeat)RepeatIndex, StartLocal = StartDate.Date.Add(StartTime),
            });
            Title = string.Empty;
            Details = string.Empty;
            Message = "Schedule saved. It runs while the app is open; overdue schedules run once on reopening.";
        });
    }

    [RelayCommand]
    private async Task RunNowAsync()
    {
        if (Selected is not { } item) return;
        await PerformAsync(async () => { await _service.RunAsync(item.Id); Message = "Run finished. Select the item to read its result."; });
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync()
    {
        if (Selected is not { } item) return;
        await PerformAsync(async () => { await _service.SetEnabledAsync(item.Id, !item.Enabled); Message = item.Enabled ? "Schedule paused." : "Schedule resumed."; });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is not { } item) return;
        await PerformAsync(async () => { await _service.DeleteAsync(item.Id); Message = "Schedule deleted."; });
    }

    private async Task PerformAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (ArgumentException exception) { Message = exception.Message; }
        catch (InvalidOperationException exception) { Message = exception.Message; }
        catch (Exception) { Message = "Could not save or run this schedule. Check local storage and configuration."; }
        finally { IsBusy = false; await RefreshAsync(); }
    }
}
