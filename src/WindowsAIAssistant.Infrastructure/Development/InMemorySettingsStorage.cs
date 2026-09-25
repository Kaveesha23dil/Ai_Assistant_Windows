using System.Collections.Concurrent;
using WindowsAIAssistant.Core.Abstractions.Storage;

namespace WindowsAIAssistant.Infrastructure.Development;

public sealed class InMemorySettingsStorage : ISettingsStorage
{
    private readonly ConcurrentDictionary<string, object?> _values = new(StringComparer.Ordinal);

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);

        if (_values.TryGetValue(key, out var value) && value is T typedValue)
        {
            return Task.FromResult<T?>(typedValue);
        }

        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(
        string key,
        T value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);

        if (value is null)
        {
            _values.TryRemove(key, out _);
        }
        else
        {
            _values[key] = value;
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        _values.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Setting key cannot be empty.", nameof(key));
        }
    }
}
