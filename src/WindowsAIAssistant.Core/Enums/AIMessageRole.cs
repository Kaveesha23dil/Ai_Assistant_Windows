namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// Identifies the role of an AI message within a conversation.
/// </summary>
public enum AIMessageRole
{
    /// <summary>System-provided instructions or context.</summary>
    System,

    /// <summary>A message sent by the end user.</summary>
    User,

    /// <summary>A message produced by the AI assistant.</summary>
    Assistant,

    /// <summary>A message produced by a tool or external integration.</summary>
    Tool
}