using CommunityToolkit.Mvvm.ComponentModel;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.App.ViewModels;

/// <summary>
/// One message in the chat transcript.
/// <para>
/// The type exists because the streaming answer is the same message the transcript shows. A
/// new assistant message is added when the request starts and its text is replaced as the
/// answer arrives, rather than a row being added per fragment, so a long answer reads as one
/// message and scrolling keeps a sensible position.
/// </para>
/// <para>
/// Only <see cref="Text"/> is mutable, and only through <see cref="Update"/>. Role and
/// timestamp are fixed when the message is created, because a message that changed its author
/// or its time would be a different message.
/// </para>
/// </summary>
public sealed class ChatMessageViewModel : ObservableObject
{
    private string _text;

    public ChatMessageViewModel(AIMessageRole role, string text, DateTimeOffset timestamp)
    {
        Role = role;
        _text = text ?? string.Empty;
        Timestamp = timestamp;
    }

    /// <summary>Gets who said it.</summary>
    public AIMessageRole Role { get; }

    /// <summary>Gets when it was said.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the text shown for this message.
    /// <para>
    /// Written by hand rather than generated because the setter has to stay private: the
    /// transcript is only allowed to be rewritten by the answer it is currently receiving.
    /// A public setter would let any caller rewrite a message that was already said, which is
    /// the one thing a transcript must not allow.
    /// </para>
    /// </summary>
    public string Text
    {
        get => _text;
        private set
        {
            // Every fragment of a streamed answer arrives here, so a fragment that adds
            // nothing must not raise a notification. Re-binding a text block for no visible
            // change is the kind of cost that turns a smooth answer into a stuttering one.
            if (string.Equals(_text, value, StringComparison.Ordinal))
            {
                return;
            }

            _text = value;
            OnPropertyChanged(nameof(Text));
        }
    }

    /// <summary>Gets a value indicating whether the person said it.</summary>
    public bool IsFromUser => Role == AIMessageRole.User;

    /// <summary>Gets a value indicating whether the assistant said it.</summary>
    public bool IsFromAssistant => Role == AIMessageRole.Assistant;

    /// <summary>
    /// Gets a value indicating whether this message is a report about the request rather than
    /// an answer, which is how a failure, a refusal, and a stop are all shown without inventing
    /// an assistant turn that never happened.
    /// </summary>
    public bool IsStatus => Role == AIMessageRole.System;

    /// <summary>Gets the heading shown above the message.</summary>
    public string RoleLabel => Role switch
    {
        AIMessageRole.User => "You",
        AIMessageRole.Assistant => "Assistant",
        _ => "Status"
    };

    /// <summary>
    /// Replaces the text of a message that is still being written. Called as the answer
    /// arrives, and once more on completion or cancellation so the message ends up holding
    /// exactly what was received.
    /// </summary>
    public void Update(string text) => Text = text ?? string.Empty;

    /// <summary>Creates a message the person typed.</summary>
    public static ChatMessageViewModel FromUser(string text) =>
        new(AIMessageRole.User, text, DateTimeOffset.Now);

    /// <summary>Creates an assistant message, which is what a streamed answer grows into.</summary>
    public static ChatMessageViewModel FromAssistant(string text) =>
        new(AIMessageRole.Assistant, text, DateTimeOffset.Now);

    /// <summary>Creates a status line, used for a failure, a refusal, or a cancellation note.</summary>
    public static ChatMessageViewModel FromStatus(string text) =>
        new(AIMessageRole.System, text, DateTimeOffset.Now);
}
