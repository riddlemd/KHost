using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services;
using KHost.Domain.Services.AuthProviders;
using KHost.Domain.Services.MediaProviders;
using KHost.Domain.Services.PasswordHashers;
using KHost.LrcLib;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.Domain
{
    public static class ProjectExtensions
    {
        public static IServiceCollection AddDomain(this IServiceCollection serviceCollection)
        {
            // Register dependent sub-libraries
            serviceCollection.AddLrcLib(options =>
            {
                options.UserAgent = "KHost/2.0 (+https://github.com/riddlemd/KHost)";
            });

            // Configure KHost Options
            serviceCollection.AddOptions<VenuesService.ServiceOptions>()
                .BindConfiguration(VenuesService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<UsersService.ServiceOptions>()
                .BindConfiguration(UsersService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<JsonFileCacheService.ServiceOptions>()
                .BindConfiguration(JsonFileCacheService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<MediaFileParsingService.ServiceOptions>()
                .BindConfiguration(MediaFileParsingService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<LocalScreenProvider.ServiceOptions>()
                .BindConfiguration(LocalScreenProvider.ServiceOptions.SectionName);

            serviceCollection.AddOptions<PlaybackService.ServiceOptions>()
                .BindConfiguration(PlaybackService.ServiceOptions.SectionName);

            // Configure KHost Services
            serviceCollection.AddSingleton(TimeProvider.System);
            serviceCollection.AddSingleton<IMediaFileParsingService, MediaFileParsingService>();
            serviceCollection.AddSingleton<IMediaImportService, MediaImportService>();
            serviceCollection.AddSingleton<ICacheService, JsonFileCacheService>();
            serviceCollection.AddSingleton<ISingerQueueService, SingerQueueService>();
            serviceCollection.AddSingleton<IPlaybackService, PlaybackService>();
            serviceCollection.AddSingleton<IMediaSearchService, MediaSearchService>();
            serviceCollection.AddSingleton<IVenuesService, VenuesService>();
            serviceCollection.AddSingleton<IUsersService, UsersService>();
            serviceCollection.AddSingleton<IPerformanceService, PerformanceService>();
            serviceCollection.AddSingleton<IMediaService, MediaService>();
            serviceCollection.AddSingleton<ILyricsService, LyricsService>();
            serviceCollection.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
            serviceCollection.AddSingleton<IAuthService, AuthService>();
            serviceCollection.AddSingleton<IAuthProvider, LocalAuthProvider>();
            serviceCollection.AddSingleton<IUserGroupsService, UserGroupsService>();
            serviceCollection.AddSingleton<ITipsService, TipsService>();

            // Screen Providers
            serviceCollection.AddSingleton<IScreenProvider, LocalScreenProvider>();

            // Media Providers
            serviceCollection.AddSingleton<IMediaProvider, DefaultMediaProvider>();

            return serviceCollection;
        }
    }
}
