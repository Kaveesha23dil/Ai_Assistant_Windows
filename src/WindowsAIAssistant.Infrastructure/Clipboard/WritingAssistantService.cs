using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Abstractions.AI;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.Infrastructure.Clipboard;

public sealed class WritingAssistantService : IWritingAssistantService
{
    private readonly IAIService? _aiService;
    private readonly ITextTransformationService _textTransformationService;

    public WritingAssistantService(IAIService? aiService, ITextTransformationService textTransformationService)
    {
        _aiService = aiService;
        _textTransformationService = textTransformationService ?? throw new ArgumentNullException(nameof(textTransformationService));
    }

    public async Task<ClipboardTransformationResult> TransformAsync(ClipboardTransformationRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return new ClipboardTransformationResult { Success = false, ErrorMessage = "Request is null." };
        }

        if (request.Content == null || string.IsNullOrWhiteSpace(request.Content.Text))
        {
            return new ClipboardTransformationResult { Success = false, ErrorMessage = "No text content to transform." };
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Handle local formatting
            if (request.Action == WritingAction.FormatJson && request.Content.Type == ClipboardContentType.Json)
            {
                var formatted = await _textTransformationService.FormatJsonAsync(request.Content.Text, cancellationToken).ConfigureAwait(false);
                return new ClipboardTransformationResult { Success = true, ResultText = formatted };
            }

            if (request.Action == WritingAction.FormatXml)
            {
                var formatted = await _textTransformationService.FormatXmlAsync(request.Content.Text, cancellationToken).ConfigureAwait(false);
                return new ClipboardTransformationResult { Success = true, ResultText = formatted };
            }

            if (request.Action == WritingAction.FormatSql)
            {
                var formatted = await _textTransformationService.FormatSqlAsync(request.Content.Text, cancellationToken).ConfigureAwait(false);
                return new ClipboardTransformationResult { Success = true, ResultText = formatted };
            }

            if (request.Action == WritingAction.FormatMarkdown)
            {
                var formatted = await _textTransformationService.FormatMarkdownAsync(request.Content.Text, cancellationToken).ConfigureAwait(false);
                return new ClipboardTransformationResult { Success = true, ResultText = formatted };
            }

            if (request.Action == WritingAction.ValidateJson)
            {
                var valid = _textTransformationService.IsValidJson(request.Content.Text);
                return new ClipboardTransformationResult { Success = true, ResultText = valid ? "Valid JSON" : "Invalid JSON" };
            }

            if (request.Action == WritingAction.ConvertJsonToTypeScript)
            {
                var result = await _textTransformationService.ConvertJsonToTypeScriptAsync(request.Content.Text, cancellationToken).ConfigureAwait(false);
                return new ClipboardTransformationResult { Success = true, ResultText = result };
            }

            if (request.Action == WritingAction.ConvertJsonToCSharp)
            {
                var result = await _textTransformationService.ConvertJsonToCSharpAsync(request.Content.Text, cancellationToken).ConfigureAwait(false);
                return new ClipboardTransformationResult { Success = true, ResultText = result };
            }

            // For AI operations, if no AI service available, return a basic transformation
            if (_aiService == null)
            {
                return new ClipboardTransformationResult { Success = false, ErrorMessage = "AI service not available." };
            }

            var prompt = BuildPrompt(request);
            var response = await _aiService.SendMessageAsync(prompt, cancellationToken).ConfigureAwait(false);
            if (response == null || string.IsNullOrWhiteSpace(response.Content))
            {
                return new ClipboardTransformationResult { Success = false, ErrorMessage = "Empty AI response." };
            }

            return new ClipboardTransformationResult { Success = true, ResultText = response.Content.Trim() };
        }
        catch (OperationCanceledException)
        {
            return new ClipboardTransformationResult { Success = false, WasCancelled = true, ErrorMessage = "Cancelled." };
        }
        catch (Exception ex)
        {
            return new ClipboardTransformationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static string BuildPrompt(ClipboardTransformationRequest request)
    {
        var action = request.Action switch
        {
            WritingAction.ImproveGrammar => "Correct grammar and spelling while preserving meaning, names, numbers, dates, and technical terms.",
            WritingAction.Rewrite => "Rewrite the text while preserving meaning and key information.",
            WritingAction.Professional => "Rewrite in a professional and polished tone.",
            WritingAction.Friendly => "Rewrite in a friendly and warm tone.",
            WritingAction.Concise => "Make the text shorter while keeping essential information.",
            WritingAction.Detailed => "Expand the text with more details while staying factual.",
            WritingAction.Summarize => "Provide a concise summary of the key points.",
            WritingAction.Expand => "Expand the text while preserving meaning.",
            WritingAction.Simplify => "Simplify the text for easier understanding.",
            WritingAction.Translate => $"Translate to {request.TargetLanguage ?? "English"} while preserving meaning.",
            WritingAction.BulletPoints => "Convert to clear bullet points.",
            WritingAction.ExtractKeyPoints => "Extract the key points.",
            WritingAction.GenerateTitle => "Generate an appropriate title.",
            WritingAction.GenerateEmail => "Convert this into a proper email with subject, greeting, body, and closing.",
            WritingAction.GenerateReply => "Generate an appropriate reply.",
            WritingAction.Explain => "Explain this content clearly.",
            WritingAction.ExplainCode => "Explain this code with inputs, outputs, logic, and potential issues.",
            WritingAction.FindBugs => "Find potential bugs and issues with location, why, and suggested fix.",
            WritingAction.ImproveCode => "Improve this code for readability and maintainability.",
            WritingAction.OptimizeCode => "Optimize this code for performance.",
            WritingAction.AddComments => "Add clear comments to this code.",
            WritingAction.GenerateTests => "Generate appropriate tests for this code.",
            WritingAction.ConvertLanguage => $"Convert this code. User instruction: {request.UserInstruction ?? ""}",
            WritingAction.GenerateDocumentation => "Generate documentation for this code.",
            WritingAction.ExplainJson => "Explain this JSON structure and fields.",
            WritingAction.FindFields => "List all fields in this JSON with descriptions.",
            WritingAction.ConvertToTable => "Convert this to a markdown table.",
            WritingAction.DescribeImage => "Describe this image in detail.",
            WritingAction.ExtractTextFromImage => "Extract all text from this image.",
            WritingAction.ExplainScreenshot => "Explain what this screenshot shows.",
            WritingAction.AnalyzeDiagram => "Analyze this diagram.",
            _ => "Transform the text as requested."
        };

        return $"Task:\n{action}\n\nContent type:\n{request.Content.Type}\n\nUser instruction:\n{request.UserInstruction ?? ""}\n\nContent:\n{request.Content.Text}\n\nDo not invent information. Preserve factual meaning, important names, numbers, dates, and technical terms unless explicitly requested to change them.";
    }

    public List<ClipboardAction> GetAvailableActions(ClipboardContentType contentType)
    {
        var actions = new List<ClipboardAction>();

        if (contentType == ClipboardContentType.Code)
        {
            actions.Add(new ClipboardAction { Id = "explaincode", Label = "Explain Code", ActionType = WritingAction.ExplainCode, Order = 1 });
            actions.Add(new ClipboardAction { Id = "findbugs", Label = "Find Bugs", ActionType = WritingAction.FindBugs, Order = 2 });
            actions.Add(new ClipboardAction { Id = "improvecode", Label = "Improve Code", ActionType = WritingAction.ImproveCode, Order = 3 });
            actions.Add(new ClipboardAction { Id = "optimize", Label = "Optimize", ActionType = WritingAction.OptimizeCode, Order = 4 });
            actions.Add(new ClipboardAction { Id = "addcomments", Label = "Add Comments", ActionType = WritingAction.AddComments, Order = 5 });
            actions.Add(new ClipboardAction { Id = "gentests", Label = "Generate Tests", ActionType = WritingAction.GenerateTests, Order = 6 });
            actions.Add(new ClipboardAction { Id = "convertlang", Label = "Convert Language", ActionType = WritingAction.ConvertLanguage, Order = 7 });
            actions.Add(new ClipboardAction { Id = "gendoc", Label = "Generate Documentation", ActionType = WritingAction.GenerateDocumentation, Order = 8 });
            return actions.OrderBy(a => a.Order).ToList();
        }

        if (contentType == ClipboardContentType.Json)
        {
            actions.Add(new ClipboardAction { Id = "formatjson", Label = "Format JSON", ActionType = WritingAction.FormatJson, RequiresAI = false, IsLocal = true, Order = 1 });
            actions.Add(new ClipboardAction { Id = "explainjson", Label = "Explain JSON", ActionType = WritingAction.ExplainJson, Order = 2 });
            actions.Add(new ClipboardAction { Id = "findfields", Label = "Find Fields", ActionType = WritingAction.FindFields, Order = 3 });
            actions.Add(new ClipboardAction { Id = "jsts", Label = "Convert to TypeScript", ActionType = WritingAction.ConvertJsonToTypeScript, RequiresAI = false, IsLocal = true, Order = 4 });
            actions.Add(new ClipboardAction { Id = "jscs", Label = "Convert to C#", ActionType = WritingAction.ConvertJsonToCSharp, RequiresAI = false, IsLocal = true, Order = 5 });
            actions.Add(new ClipboardAction { Id = "jsontable", Label = "Convert to Table", ActionType = WritingAction.ConvertToTable, Order = 6 });
            actions.Add(new ClipboardAction { Id = "validatejson", Label = "Validate JSON", ActionType = WritingAction.ValidateJson, RequiresAI = false, IsLocal = true, Order = 7 });
            return actions.OrderBy(a => a.Order).ToList();
        }

        if (contentType == ClipboardContentType.Url)
        {
            actions.Add(new ClipboardAction { Id = "openurl", Label = "Open URL", ActionType = WritingAction.OpenUrl, RequiresAI = false, Order = 1 });
            actions.Add(new ClipboardAction { Id = "sumurl", Label = "Summarize Website", ActionType = WritingAction.SummarizeWebsite, Order = 2 });
            actions.Add(new ClipboardAction { Id = "expurl", Label = "Explain URL", ActionType = WritingAction.ExplainUrl, Order = 3 });
            actions.Add(new ClipboardAction { Id = "copydom", Label = "Copy Domain", ActionType = WritingAction.CopyDomain, RequiresAI = false, Order = 4 });
            actions.Add(new ClipboardAction { Id = "searchrel", Label = "Search Related Information", ActionType = WritingAction.SearchRelated, Order = 5 });
            return actions.OrderBy(a => a.Order).ToList();
        }

        if (contentType == ClipboardContentType.Image)
        {
            actions.Add(new ClipboardAction { Id = "descimg", Label = "Describe Image", ActionType = WritingAction.DescribeImage, Order = 1 });
            actions.Add(new ClipboardAction { Id = "extracttxt", Label = "Extract Text", ActionType = WritingAction.ExtractTextFromImage, Order = 2 });
            actions.Add(new ClipboardAction { Id = "explainscr", Label = "Explain Screenshot", ActionType = WritingAction.ExplainScreenshot, Order = 3 });
            actions.Add(new ClipboardAction { Id = "analyzediag", Label = "Analyze Diagram", ActionType = WritingAction.AnalyzeDiagram, Order = 4 });
            actions.Add(new ClipboardAction { Id = "askimg", Label = "Ask Question", ActionType = WritingAction.AskQuestionAboutImage, Order = 5 });
            return actions.OrderBy(a => a.Order).ToList();
        }

        if (contentType == ClipboardContentType.File)
        {
            actions.Add(new ClipboardAction { Id = "sumfiles", Label = "Summarize", ActionType = WritingAction.SummarizeFiles, Order = 1 });
            actions.Add(new ClipboardAction { Id = "anfiles", Label = "Analyze", ActionType = WritingAction.AnalyzeFiles, Order = 2 });
            actions.Add(new ClipboardAction { Id = "compfiles", Label = "Compare", ActionType = WritingAction.CompareFiles, Order = 3 });
            actions.Add(new ClipboardAction { Id = "finddiff", Label = "Find Differences", ActionType = WritingAction.FindDifferences, Order = 4 });
            actions.Add(new ClipboardAction { Id = "exinfo", Label = "Extract Information", ActionType = WritingAction.ExtractInformation, Order = 5 });
            actions.Add(new ClipboardAction { Id = "combrep", Label = "Create Combined Report", ActionType = WritingAction.CreateCombinedReport, Order = 6 });
            return actions.OrderBy(a => a.Order).ToList();
        }

        // Default text actions
        actions.Add(new ClipboardAction { Id = "impwrite", Label = "Improve Writing", ActionType = WritingAction.ImproveGrammar, Order = 1 });
        actions.Add(new ClipboardAction { Id = "summarize", Label = "Summarize", ActionType = WritingAction.Summarize, Order = 2 });
        actions.Add(new ClipboardAction { Id = "pro", Label = "Make Professional", ActionType = WritingAction.Professional, Order = 3 });
        actions.Add(new ClipboardAction { Id = "shorter", Label = "Make Shorter", ActionType = WritingAction.Concise, Order = 4 });
        actions.Add(new ClipboardAction { Id = "friend", Label = "Make Friendly", ActionType = WritingAction.Friendly, Order = 5 });
        actions.Add(new ClipboardAction { Id = "translate", Label = "Translate", ActionType = WritingAction.Translate, Order = 6 });
        actions.Add(new ClipboardAction { Id = "explain", Label = "Explain", ActionType = WritingAction.Explain, Order = 7 });
        actions.Add(new ClipboardAction { Id = "bullet", Label = "Convert to Bullet Points", ActionType = WritingAction.BulletPoints, Order = 8 });
        actions.Add(new ClipboardAction { Id = "email", Label = "Generate Email", ActionType = WritingAction.GenerateEmail, Order = 9 });
        actions.Add(new ClipboardAction { Id = "reply", Label = "Generate Reply", ActionType = WritingAction.GenerateReply, Order = 10 });
        return actions.OrderBy(a => a.Order).ToList();
    }
}
