using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// Automations page state. No workflow execution exists in this step.
/// </summary>
public sealed partial class AutomationsViewModel : ObservableObject
{
    [RelayCommand]
    private void CreateAutomation()
    {
    }

    public string HeaderTitle => "Automations";

    public string Description => "Create AI-powered workflows for recurring tasks.";

    public string EmptyStateTitle => "No automations yet.";

    public string EmptyStateMessage => "Create your first automation to see it listed here.";
}
