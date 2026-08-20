using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services;
using KHost.Domain.Services.AuthProviders;
using KHost.Domain.Services.MediaProviders;
using KHost.Abstractions.Services.QueueRotation;
using KHost.Domain.Services.PasswordHashers;
using KHost.Domain.Services.Plugins;
using KHost.Domain.Services.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modes;
using KHost.LrcLib;
using KHost.Plugins.Sdk.Services;
using KHost.Plugins.Sdk.Services.QueueRotation;
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
            serviceCollection.AddOptions<MediaFileParsingService.ServiceOptions>()
                .BindConfiguration(MediaFileParsingService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<LocalScreenProvider.ServiceOptions>()
                .BindConfiguration(LocalScreenProvider.ServiceOptions.SectionName);

            serviceCollection.AddOptions<PlaybackService.ServiceOptions>()
                .BindConfiguration(PlaybackService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<HlsMediaStreamService.ServiceOptions>()
                .BindConfiguration(HlsMediaStreamService.ServiceOptions.SectionName);

            // Configure KHost Services
            serviceCollection.AddSingleton(TimeProvider.System);
            serviceCollection.AddSingleton<IMediaFileParsingService, MediaFileParsingService>();
            serviceCollection.AddSingleton<IMediaFingerprintService, MediaFingerprintService>();
            serviceCollection.AddSingleton<IMediaImportService, MediaImportService>();
            serviceCollection.AddSingleton<ICacheService, JsonFileCacheService>();
            serviceCollection.AddSingleton<ISingerQueueService, SingerQueueService>();
            serviceCollection.AddSingleton<IMediaStreamService, HlsMediaStreamService>();
            serviceCollection.AddSingleton<IScreenCoordinationService, ScreenCoordinationService>();
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
            serviceCollection.AddSingleton<IMediaProvider, LocalMediaProvider>();

            // Queue Rotation (built-in modes register before plugins so their ids win)
            serviceCollection.AddSingleton<IQueueRotationStrategyFactory, QueueRotationStrategyFactory>();
            serviceCollection.AddSingleton<IQueueRotationMode, FifoStrategy>();
            serviceCollection.AddSingleton<IQueueRotationMode, RoundRobinStrategy>();
            serviceCollection.AddSingleton<IQueueRotationMode, ReverseStrategy>();
            serviceCollection.AddSingleton<IQueueRotationMode, LongestWaitFirstStrategy>();
            serviceCollection.AddSingleton<IQueueRotationMode, FewestSongsFirstStrategy>();
            serviceCollection.AddSingleton<IQueueRotationMode, WeightedFairStrategy>();
            serviceCollection.AddSingleton<IQueueRotationMode, WeightedLotteryStrategy>();
            serviceCollection.AddSingleton<IQueueRotationMode, PureLotteryStrategy>();
            serviceCollection.AddSingleton<IQueueRotationMode, ShuffleBucketStrategy>();
            // The modifiers are missing on purpose, not overlooked: they decorate whichever mode a
            // venue picked, so QueueRotationStrategyFactory.Resolve composes them from its config.

            return serviceCollection;
        }

        /// <summary>
        /// Discovers and loads enabled plugins. Must run before the container is built;
        /// changes to the plugins folder or enabled list apply on restart.
        /// </summary>
        public static IServiceCollection AddPlugins(this IServiceCollection serviceCollection)
        {
            var state = PluginLoader.ReadState(Path.Combine(AppContext.BaseDirectory, "cache"));
            var plugins = PluginLoader.Discover(Path.Combine(AppContext.BaseDirectory, "plugins"), state);

            PluginLoader.LoadAndRegister(serviceCollection, plugins, state);

            serviceCollection.AddSingleton<IPluginRegistry>(new PluginRegistry(plugins));
            serviceCollection.AddSingleton<IPluginsService, PluginsService>();

            return serviceCollection;
        }
    }
}
