using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.Cast;

public static class ProjectExtensions
{
    public static IServiceCollection AddCast(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddOptions<CastScreenTransport.ServiceOptions>()
            .BindConfiguration(CastScreenTransport.ServiceOptions.SectionName);

        serviceCollection.AddSingleton<CastScreenTransport>();

        // One instance wearing both hats: the transport the screen server fans out over, and the
        // discovery surface the screens page attaches devices through.
        serviceCollection.AddSingleton<IScreenTransport>(sp => sp.GetRequiredService<CastScreenTransport>());
        serviceCollection.AddSingleton<ICastScreenService>(sp => sp.GetRequiredService<CastScreenTransport>());

        return serviceCollection;
    }
}
