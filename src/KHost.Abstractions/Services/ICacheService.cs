namespace KHost.Abstractions.Services;

public interface ICacheService
{
    Task<T?> LoadAsync<T>(string key);
    Task SaveAsync<T>(string key, T state);
}
