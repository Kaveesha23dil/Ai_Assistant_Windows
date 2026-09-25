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
        string? errorMessage)
    {
        Content = content;
        Provider = provider;
        Model = model;
        CreatedAt = createdAt;
        IsSuccessful = isSuccessful;
        ErrorMessage = errorMessage;
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

    /// <summary>Creates a successful response.</summary>
    public static AIResponse Success(string content, AIProviderType provider, string? model = null)
        => new(content, provider, model, DateTimeOffset.UtcNow, isSuccessful: true, errorMessage: null);

    /// <summary>Creates a failed response.</summary>
    public static AIResponse Failure(string errorMessage, AIProviderType provider, string? model = null)
        => new(string.Empty, provider, model, DateTimeOffset.UtcNow, isSuccessful: false, errorMessage);
}