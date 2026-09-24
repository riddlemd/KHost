namespace KHost.Abstractions.Services;

/// <summary>One timed span of work, from <see cref="IAnalyticsService.StartActivity"/> to
/// <see cref="IDisposable.Dispose"/>.</summary>
/// <remarks>When nothing is collecting traces every call silently does nothing, so code need not
/// check whether telemetry is on. Dispose it, typically with <c>using</c>, to end the span.</remarks>
public interface IAnalyticsActivity : IDisposable
{
    /// <summary>Attaches a key/value to the span, for filtering traces later.</summary>
    void SetTag(string key, object? value);

    /// <summary>Marks the span as having succeeded.</summary>
    void SetSuccess();

    /// <summary>Marks the span as failed.</summary>
    /// <param name="reason">Recorded with the span when given.</param>
    void SetError(string? reason = null);
}
