using System.Text.Json;
using AstroDesk.Core.Entities;
using AstroDesk.Core.Interfaces;

namespace AstroDesk.Core.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General);
    private readonly ISettingsRepository _repository;

    public SettingsService(ISettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<T?> GetAsync<T>(
        string key,
        T? defaultValue = default,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        var setting = await _repository.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (setting is null)
        {
            return defaultValue;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(setting.Value, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The persisted value for setting '{key}' is not valid {typeof(T).Name} JSON.",
                exception);
        }
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        var serializedValue = JsonSerializer.Serialize(value, SerializerOptions);
        var valueType = typeof(T).FullName ?? typeof(T).Name;
        var existing = await _repository.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            existing = new AppSetting(key, serializedValue, valueType, category);
        }
        else
        {
            existing.SetValue(serializedValue, valueType, category);
        }

        await _repository.SetAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        return _repository.RemoveAsync(key, cancellationToken);
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
    }
}
