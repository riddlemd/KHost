using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.MediaProviders;
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

            serviceCollection.AddOptions<SingersService.ServiceOptions>()
                .BindConfiguration(SingersService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<JsonFileCacheService.ServiceOptions>()
                .BindConfiguration(JsonFileCacheService.ServiceOptions.SectionName);

            // Configure KHost Services
            serviceCollection.AddSingleton(TimeProvider.System);
            serviceCollection.AddSingleton<ICacheService, JsonFileCacheService>();
            serviceCollection.AddSingleton<ISingerQueueService, SingerQueueService>();
            serviceCollection.AddSingleton<IPlaybackService, PlaybackService>();
            serviceCollection.AddSingleton<IMediaSearchService, MediaSearchService>();
            serviceCollection.AddSingleton<IVenuesService, VenuesService>();
            serviceCollection.AddSingleton<ISingersService, SingersService>();
            serviceCollection.AddSingleton<IPerformanceService, PerformanceService>();
            serviceCollection.AddSingleton<IMediaService, MediaService>();

            // Media Providers
            serviceCollection.AddSingleton<IMediaProvider, DefaultMediaProvider>();

            return serviceCollection;
        }
    }
}
