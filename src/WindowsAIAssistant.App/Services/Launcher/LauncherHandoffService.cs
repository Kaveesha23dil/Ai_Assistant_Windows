namespace WindowsAIAssistant.App.Services.Launcher;

/// <summary>
/// Carries one launcher question to the chat page. The launcher hides before navigation, so
/// this small mailbox is what lets the chat view model pick the question up whether it is
/// already open or is created afterwards.
/// </summary>
public sealed class LauncherHandoffService
{
    private readonly object _gate = new();
    private string? _pendingQuestion;

    /// <summary>Raised after a question has been left for the chat page.</summary>
    public event EventHandler? QuestionPending;

    /// <summary>Leaves a question for the chat page and tells anyone listening.</summary>
    public void PostQuestion(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return;
        }

        lock (_gate)
        {
            _pendingQuestion = question.Trim();
        }

        QuestionPending?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes the pending question, if there is one.</summary>
    public bool TryTake(out string? question)
    {
        lock (_gate)
        {
            question = _pendingQuestion;
            _pendingQuestion = null;
            return question is not null;
        }
    }
}
