using Microsoft.Extensions.Logging.Abstractions;
using WindowsAIAssistant.Application.AI.Services;
using WindowsAIAssistant.Core.Abstractions.AI;

namespace WindowsAIAssistant.Application.Tests.Helpers;

/// <summary>
/// Assembles the collaborators the AI handlers need, so a test can say what it is checking
/// instead of repeating four constructor calls.
/// <para>
/// The prompt and defaults are the real production types, not doubles. A double for the prompt
/// provider would let a test pass while the wiring that supplies the prompt was wrong, and the
/// prompt is the one piece of text that decides how the assistant behaves.
/// </para>
/// </summary>
internal static class AITestHarness
{
    /// <summary>The system prompt used by tests that do not care what it says.</summary>
    public const string SystemPrompt = "You are a test assistant.";

    /// <summary>Builds the conversation store the handlers write to.</summary>
    public static ConversationService CreateConversations() =>
        new(NullLogger<ConversationService>.Instance);

    /// <summary>Builds the context builder, with a history limit a test can set.</summary>
    public static AIConversationContextBuilder CreateContextBuilder(
        string? systemPrompt = null,
        int maxConversationMessages = 20) =>
        new(
            new StaticSystemPromptProvider(systemPrompt ?? SystemPrompt),
            new StaticAIRequestDefaults(
                model: "test-model",
                requestTimeout: TimeSpan.FromSeconds(30),
                useStreaming: false,
                maxConversationMessages: maxConversationMessages));

    /// <summary>Builds the defaults a coordinator under test should read.</summary>
    public static StaticAIRequestDefaults CreateDefaults(
        bool useStreaming = false,
        TimeSpan? requestTimeout = null,
        int maxConversationMessages = 20) =>
        new(
            model: "test-model",
            requestTimeout: requestTimeout ?? TimeSpan.FromSeconds(30),
            useStreaming: useStreaming,
            maxConversationMessages: maxConversationMessages);
}
