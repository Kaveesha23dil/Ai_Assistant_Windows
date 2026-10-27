using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.Application.Clipboard;

public sealed class ClipboardPromptBuilder
{
    public string Build(ClipboardTransformationRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var sb = new StringBuilder();
        sb.AppendLine("Task:");
        sb.AppendLine(GetTaskDescription(request.Action, request));
        sb.AppendLine();
        sb.AppendLine("Content type:");
        sb.AppendLine(request.Content?.Type.ToString() ?? "Unknown");
        sb.AppendLine();
        sb.AppendLine("User instruction:");
        sb.AppendLine(request.UserInstruction ?? string.Empty);
        sb.AppendLine();
        sb.AppendLine("Content:");
        sb.AppendLine(request.Content?.Text ?? string.Empty);
        sb.AppendLine();
        sb.AppendLine("Preserve factual meaning, important names, numbers, dates, and technical terms unless explicitly requested.");
        sb.AppendLine("Do not invent information.");
        return sb.ToString();
    }

    private static string GetTaskDescription(WritingAction action, ClipboardTransformationRequest request)
    {
        return action switch
        {
            WritingAction.ImproveGrammar => "Rewrite grammatically correct while preserving meaning, names, numbers, dates, and technical terms.",
            WritingAction.Rewrite => "Rewrite while preserving meaning.",
            WritingAction.Professional => "Make professional.",
            WritingAction.Friendly => "Make friendly.",
            WritingAction.Concise => "Make shorter.",
            WritingAction.Detailed => "Make longer and more detailed.",
            WritingAction.Summarize => "Summarize.",
            WritingAction.Expand => "Expand.",
            WritingAction.Simplify => "Simplify.",
            WritingAction.Translate => $"Translate to {request.TargetLanguage ?? "English"}.",
            WritingAction.BulletPoints => "Convert to bullet points.",
            WritingAction.ExtractKeyPoints => "Extract key points.",
            WritingAction.GenerateTitle => "Generate title.",
            WritingAction.GenerateEmail => "Turn this into an email with subject, greeting, body, closing.",
            WritingAction.GenerateReply => "Write a reply.",
            WritingAction.Explain => "Explain.",
            WritingAction.ExplainCode => "Explain code: inputs, outputs, logic, potential issues.",
            WritingAction.FindBugs => "Find bugs with location, why, suggested fix.",
            WritingAction.ImproveCode => "Improve code.",
            WritingAction.OptimizeCode => "Optimize code.",
            WritingAction.AddComments => "Add comments.",
            WritingAction.GenerateTests => "Generate tests.",
            WritingAction.ConvertLanguage => "Convert language.",
            WritingAction.GenerateDocumentation => "Generate documentation.",
            _ => "Transform."
        };
    }
}
