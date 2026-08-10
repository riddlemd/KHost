namespace KHost.Abstractions.Services;

public interface IAnalyticsActivity : IDisposable
{
    void SetTag(string key, object? value);
    void SetSuccess();
    void SetError(string? reason = null);
}
