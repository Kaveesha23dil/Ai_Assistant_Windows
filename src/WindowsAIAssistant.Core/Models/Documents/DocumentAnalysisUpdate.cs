namespace WindowsAIAssistant.Core.Models.Documents;

/// <summary>
/// One step of a streamed answer about a document.
/// </summary>
public sealed record DocumentAnalysisUpdate
{
    private DocumentAnalysisUpdate(
        DocumentAnalysisUpdateKind kind,
        string text,
        DocumentAnalysisResult? result,
        Core.Enums.AIProviderType? provider,
        string? model,
        string? errorCode,
        string? errorMessage)
    {
        Kind = kind;
        Text = text;
        Result = result;
        Provider = provider;
        Model = model;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public DocumentAnalysisUpdateKind Kind { get; }
    public string Text { get; }
    public DocumentAnalysisResult? Result { get; }
    public Core.Enums.AIProviderType? Provider { get; }
    public string? Model { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static DocumentAnalysisUpdate Started(Core.Enums.AIProviderType provider, string? model) =>
        new(DocumentAnalysisUpdateKind.Started, string.Empty, null, provider, model, null, null);

    public static DocumentAnalysisUpdate Delta(string text) =>
        new(DocumentAnalysisUpdateKind.Delta, text, null, null, null, null, null);

    public static DocumentAnalysisUpdate Completed(DocumentAnalysisResult result) =>
        new(DocumentAnalysisUpdateKind.Completed, result.Text, result, null, null, null, null);

    public static DocumentAnalysisUpdate Failed(string errorCode, string errorMessage) =>
        new(DocumentAnalysisUpdateKind.Failed, string.Empty, null, null, null, errorCode, errorMessage);
}
