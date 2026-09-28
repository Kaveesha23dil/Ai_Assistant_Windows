using WindowsAIAssistant.Application.DTOs;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Application.Documents.Handlers;

/// <summary>
/// Turns what the analysis service found into what the interface shows.
/// <para>
/// Kept in one place so the two entry points, a summary and a question, cannot end up
/// presenting the same document differently, and so that nothing which should not reach a view
/// model is carried through the gap.
/// </para>
/// </summary>
internal static class DocumentAnalysisMapper
{
    public static DocumentAnalysisDto ToDto(DocumentAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new DocumentAnalysisDto
        {
            IsSuccess = result.IsSuccess,
            Operation = result.Operation,
            FileName = result.FileName,
            FileType = result.FileType,
            Text = result.Text,
            References = result.References,
            Warnings = [.. result.Warnings.Select(DocumentWarningDto.From)],
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage,
        };
    }
}
