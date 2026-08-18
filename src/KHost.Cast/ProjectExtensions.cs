using KHost.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.Cast;

public static class ProjectExtensions
{
    public static IServiceCollection AddCast(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddOptions<CastService.ServiceOptions>()
            .BindConfiguration(CastService.ServiceOptions.SectionName);

        serviceCollection.AddSingleton<ICastService, CastService>();

        return serviceCollection;
    }
}
