using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models.Voice;

/// <summary>Raised whenever the voice assistant moves between lifecycle states.</summary>
public sealed class VoiceStateChangedEventArgs : EventArgs
{
    public VoiceStateChangedEventArgs(VoiceAssistantState previous, VoiceAssistantState current)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>Gets the state the assistant left.</summary>
    public VoiceAssistantState Previous { get; }

    /// <summary>Gets the state the assistant entered.</summary>
    public VoiceAssistantState Current { get; }
}
