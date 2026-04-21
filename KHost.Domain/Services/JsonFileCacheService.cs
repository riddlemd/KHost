using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace KHost.Domain.Services;

public class JsonFileCacheService : ICacheService
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<JsonFileCacheService> _logger;

    public IOptionsMonitor<ServiceOptions> Options { get; }

    public JsonFileCacheService(ILogger<JsonFileCacheService> logger, IOptionsMonitor<ServiceOptions> options)
    {
        _logger = logger;
        Options = options;
    }

    public async Task<T?> LoadAsync<T>(string key)
    {
        var filePath = GetCacheLocation(key);

        try
        {
            if (!File.Exists(filePath))
                return default;

            var json = await File.ReadAllTextAsync(filePath);

            return JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cache from {FilePath}", filePath);
            return default;
        }
    }

    public async Task SaveAsync<T>(string key, T state)
    {
        var filePath = GetCacheLocation(key);

        await _lock.WaitAsync();

        try
        {
            var json = JsonSerializer.Serialize(state, JsonSerializerOptions.Web);
            var directory = Path.GetDirectoryName(filePath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save cache to {FilePath}", filePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    private string GetCacheLocation(string key)
        => Path.Combine(AppContext.BaseDirectory, "cache", JsonNamingPolicy.KebabCaseLower.ConvertName(key) + ".json");

    public class ServiceOptions
    {
        public const string SectionName = nameof(JsonFileCacheService);
    }
}
