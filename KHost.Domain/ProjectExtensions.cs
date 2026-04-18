using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.Domain
{
    public static class ProjectExtensions
    {
        public static IServiceCollection AddDomain(this IServiceCollection serviceCollection)
        {
            // Configure KHost Options
            serviceCollection.AddOptions<PlaybackService.ServiceOptions>()
                .BindConfiguration(PlaybackService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<SingerQueueService.ServiceOptions>()
                .BindConfiguration(SingerQueueService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<VenuesService.ServiceOptions>()
                .BindConfiguration(VenuesService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<JsonFileCacheService.ServiceOptions>()
                .BindConfiguration(JsonFileCacheService.ServiceOptions.SectionName);

            // Configure KHost Services
            serviceCollection.AddSingleton<ICacheService, JsonFileCacheService>();
            serviceCollection.AddSingleton<ISingerQueueService, SingerQueueService>();
            serviceCollection.AddSingleton<IPlaybackService, PlaybackService>();
            serviceCollection.AddSingleton<ISongSearchService, SongSearchService>();
            serviceCollection.AddSingleton<IVenuesService, VenuesService>();

            return serviceCollection;
        }
    }
}
