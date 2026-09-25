namespace WindowsAIAssistant.Core.Abstractions.Storage;

/// <summary>
/// Persists key-based application settings. The storage technology (SQLite, JSON, etc.)
/// is deliberately left to the implementation layer.
/// </summary>
public interface ISettingsStorage
{
    /// <summary>Gets the strongly typed value associated with the specified key.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores a strongly typed value under the specified key.</summary>
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    /// <summary>Removes the value associated with the specified key, if present.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}