using System.Diagnostics;
using KHost.Abstractions.Services;

namespace KHost.Telemetry.Services;

internal sealed class OtelAnalyticsActivity(Activity? activity) : IAnalyticsActivity
{
    public void SetTag(string key, object? value) => activity?.SetTag(key, value);

    public void SetSuccess() => activity?.SetStatus(ActivityStatusCode.Ok);

    public void SetError(string? reason = null)
    {
        activity?.SetStatus(ActivityStatusCode.Error);
        if (reason is not null)
            activity?.SetTag("failure_reason", reason);
    }

    public void Dispose() => activity?.Dispose();
}
