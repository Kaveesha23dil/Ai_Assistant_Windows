using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.Core.Abstractions.Clipboard;

public interface ITextTransformationService
{
    Task<string> FormatJsonAsync(string json, CancellationToken cancellationToken = default);
    Task<string> FormatXmlAsync(string xml, CancellationToken cancellationToken = default);
    Task<string> FormatSqlAsync(string sql, CancellationToken cancellationToken = default);
    Task<string> FormatMarkdownAsync(string markdown, CancellationToken cancellationToken = default);
    bool IsValidJson(string json);
    Task<string> ConvertJsonToTypeScriptAsync(string json, CancellationToken cancellationToken = default);
    Task<string> ConvertJsonToCSharpAsync(string json, CancellationToken cancellationToken = default);
}
