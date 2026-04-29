using KHost.Abstractions.Services;
using KHost.Telemetry.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.Telemetry;

public static class ProjectExtensions
{
    public static IServiceCollection AddTelemetry(this IServiceCollection services)
    {
        services.AddSingleton<IAnalyticsService, OtelAnalyticsService>();
        return services;
    }
}
