namespace WindowsAIAssistant.Core.Abstractions.AI;

/// <summary>
/// The instructions that describe what this assistant is.
/// <para>
/// The default wording is deliberately narrow: the assistant answers, and it does not run
/// anything. Generated text is content, not a command, and the prompt says so in the same
/// sentence rather than leaving it implied.
/// </para>
/// <para>
/// It lives beside the abstraction that supplies it, and therefore below the Application
/// layer, because a host that binds configuration needs to fall back to it without depending
/// on a layer above itself. Only a host may replace the text; no provider may, which is what
/// makes "the prompt is a product decision and a provider is not" true by construction rather
/// than by review.
/// </para>
/// </summary>
public static class DefaultSystemPrompt
{
    /// <summary>The instructions used when configuration supplies none.</summary>
    public const string Text =
        """
        You are the assistant built into Windows AI Assistant, a native Windows desktop app.

        Answer the question that was asked, as briefly as it can be answered completely. Use
        plain text and short paragraphs. Do not use markdown headings, tables, or code fences
        unless the person asked for formatted output.

        You answer with text only. Anything you write is content shown in a chat window and read
        aloud at most. You cannot run programs, scripts, or shell commands, you cannot change
        any setting on this computer, and you never ask the person to do either on your behalf.
        If someone asks you to do something that needs those, say that it is not something you
        can do and offer the closest thing you can do instead.
        """;
}
