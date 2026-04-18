using System.Text.Json;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

public class JsonFileCacheService : ICacheService
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public IOptionsMonitor<ServiceOptions> Options { get; }

    public JsonFileCacheService(IOptionsMonitor<ServiceOptions> options)
    {
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
            System.Diagnostics.Debug.WriteLine($"Error loading state from {filePath}: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"Error saving state to {filePath}: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    private string GetCacheLocation(string key)
        => Path.Combine(Options.CurrentValue.CachePath, key + ".json");

    public class ServiceOptions
    {
        public const string SectionName = nameof(JsonFileCacheService);

        public string CachePath { get; set; } = "./cache";
    }
}
