using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Core.Models;

/// <summary>
/// A provider-independent response produced by an AI service.
/// </summary>
public sealed record AIResponse
{
    public AIResponse(
        string content,
        AIProviderType provider,
        string? model,
        DateTimeOffset createdAt,
        bool isSuccessful,
        string? errorMessage,
        string? errorCode = null)
    {
        Content = content;
        Provider = provider;
        Model = model;
        CreatedAt = createdAt;
        IsSuccessful = isSuccessful;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
    }

    /// <summary>Gets the response content.</summary>
    public string Content { get; }

    /// <summary>Gets the provider that produced the response.</summary>
    public AIProviderType Provider { get; }

    /// <summary>Gets the model identifier used, when known.</summary>
    public string? Model { get; }

    /// <summary>Gets the point in time the response was created.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets a value indicating whether the request completed successfully.</summary>
    public bool IsSuccessful { get; }

    /// <summary>Gets a user-safe error message when the request failed; otherwise <see langword="null"/>.</summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets the stable, user-safe code for the failure when the request failed; otherwise
    /// <see langword="null"/>. A code is what lets a caller distinguish, say, a missing key
    /// from a rate limit without parsing an English sentence.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>Gets a value indicating whether the response carries any text at all.</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Content);

    /// <summary>Creates a successful response.</summary>
    public static AIResponse Success(string content, AIProviderType provider, string? model = null)
        => new(content, provider, model, DateTimeOffset.UtcNow, isSuccessful: true, errorMessage: null);

    /// <summary>
    /// Creates a failed response.
    /// <para>
    /// There is deliberately no overload taking a model name in the third position. There used
    /// to be, and it made <c>Failure("x", provider, someCode)</c> compile as a model rather
    /// than a code without any warning, so a failure could be produced that reported no code at
    /// all while appearing to set one. A third string now always means the code.
    /// </para>
    /// </summary>
    public static AIResponse Failure(string errorMessage, AIProviderType provider)
        => Failure(errorMessage, provider, ErrorCodes.AiRequestFailed);

    /// <summary>Creates a failed response carrying the stable code for the failure.</summary>
    public static AIResponse Failure(
        string errorMessage,
        AIProviderType provider,
        string errorCode,
        string? model = null)
        => new(string.Empty, provider, model, DateTimeOffset.UtcNow, isSuccessful: false, errorMessage, errorCode);
}