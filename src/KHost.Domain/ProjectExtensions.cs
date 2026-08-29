using KHost.Plugins.Sdk.Messaging;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.AuthProviders;
using KHost.Domain.Services.Ads;
using KHost.Domain.Services.BreakMusic;
using KHost.Domain.Services.MediaPools;
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
            serviceCollection.AddLrcLib(options =>
            {
                options.UserAgent = "KHost/2.0 (+https://github.com/riddlemd/KHost)";
            });

            serviceCollection.AddOptions<MediaFileParsingService.ServiceOptions>()
                .BindConfiguration(MediaFileParsingService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<LocalScreenProvider.ServiceOptions>()
                .BindConfiguration(LocalScreenProvider.ServiceOptions.SectionName);

            serviceCollection.AddOptions<PlaybackService.ServiceOptions>()
                .BindConfiguration(PlaybackService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<HlsMediaStreamService.ServiceOptions>()
                .BindConfiguration(HlsMediaStreamService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<PluginLibrary.ServiceOptions>()
                .BindConfiguration(PluginLibrary.ServiceOptions.SectionName);

            serviceCollection.AddOptions<AdService.ServiceOptions>()
                .BindConfiguration(AdService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<PluginCatalogService.ServiceOptions>()
                .BindConfiguration(PluginCatalogService.ServiceOptions.SectionName);

            // Named clients, not typed ones: AddHttpClient<TInterface, TImplementation> registers
            // the service as transient, and every domain service here is a singleton.
            serviceCollection.AddHttpClient(PluginCatalogService.HttpClientName, http =>
            {
                http.Timeout = TimeSpan.FromSeconds(20);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("KHost/2.0 (+https://github.com/riddlemd/KHost)");
            });

            serviceCollection.AddHttpClient(PluginInstallerService.HttpClientName, http =>
            {
                // No overall timeout: it would cap the whole download, not the time between bytes.
                http.Timeout = Timeout.InfiniteTimeSpan;
                http.DefaultRequestHeaders.UserAgent.ParseAdd("KHost/2.0 (+https://github.com/riddlemd/KHost)");
            });

            serviceCollection.AddSingleton(TimeProvider.System);
            serviceCollection.AddSingleton<IMessageBroker, MessageBroker>();
            serviceCollection.AddSingleton<IFlashService, FlashService>();
        serviceCollection.AddSingleton<IMediaFileParsingService, MediaFileParsingService>();
            serviceCollection.AddSingleton<IMediaFingerprintService, MediaFingerprintService>();
            serviceCollection.AddSingleton<IMediaImportService, MediaImportService>();
            serviceCollection.AddSingleton<ICacheService, JsonFileCacheService>();
            serviceCollection.AddSingleton<ISingerQueueService, SingerQueueService>();
            serviceCollection.AddSingleton<IMediaStreamService, HlsMediaStreamService>();
            serviceCollection.AddSingleton<IAudioTrackService, AudioTrackService>();
            serviceCollection.AddSingleton<IScreenCoordinationService, ScreenCoordinationService>();
            serviceCollection.AddSingleton<IPlaybackService, PlaybackService>();
            serviceCollection.AddSingleton<IMediaSearchService, MediaSearchService>();
            serviceCollection.AddSingleton<IVenuesService, VenuesService>();
            serviceCollection.AddSingleton<IUsersService, UsersService>();
            serviceCollection.AddSingleton<IPerformanceService, PerformanceService>();
            serviceCollection.AddSingleton<IMediaService, MediaService>();
            serviceCollection.AddSingleton<IMediaPoolService, MediaPoolService>();
            serviceCollection.AddSingleton<IBreakMusicProvider, LibraryBreakMusicProvider>();
            serviceCollection.AddSingleton<IBreakMusicService, BreakMusicService>();
            serviceCollection.AddSingleton<IAdService, AdService>();
            serviceCollection.AddSingleton<ILyricsService, LyricsService>();
            serviceCollection.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
            serviceCollection.AddSingleton<IAuthService, AuthService>();
            serviceCollection.AddSingleton<IAuthProvider, LocalAuthProvider>();
            serviceCollection.AddSingleton<IUserGroupsService, UserGroupsService>();
            serviceCollection.AddSingleton<ITipsService, TipsService>();

            serviceCollection.AddSingleton<IScreenProvider, LocalScreenProvider>();
            serviceCollection.AddSingleton<IScreenKeyStore, FileScreenKeyStore>();

            serviceCollection.AddSingleton<IMediaProvider, LocalMediaProvider>();
            // Registered ahead of PluginLibrary and independent of it: PluginLibrary depends on
            // IPerformanceService, and PerformanceService depends on IDownloadsService, so the
            // service has to be its own dependency-free singleton to avoid a constructor cycle.
            serviceCollection.AddSingleton<IDownloadsService, DownloadsService>();
            serviceCollection.AddSingleton<IPluginStagingArea>(new PluginStagingArea(PluginPaths.Plugins, PluginPaths.Staging));
            serviceCollection.AddSingleton<IPluginPayloadReader, PluginPayloadReader>();
            serviceCollection.AddSingleton<IPluginCatalogService, PluginCatalogService>();
            serviceCollection.AddSingleton<IPluginInstallerService, PluginInstallerService>();
            serviceCollection.AddSingleton<PluginLibrary>();
            serviceCollection.AddSingleton<IPluginLibrary>(sp => sp.GetRequiredService<PluginLibrary>());

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
            // Before Discover, so a payload staged by the last run is a plugin this one can see.
            // Its own instance: the container that holds the singleton does not exist yet, and the
            // staging area keeps no state outside the folder, so the two agree regardless.
            new PluginStagingArea(PluginPaths.Plugins, PluginPaths.Staging).ApplyPending();

            var state = PluginLoader.ReadState(PluginPaths.Cache);
            var plugins = PluginLoader.Discover(PluginPaths.Plugins, state);

            PluginLoader.LoadAndRegister(serviceCollection, plugins, state);

            serviceCollection.AddSingleton<IPluginRegistry>(new PluginRegistry(plugins));
            serviceCollection.AddSingleton<IPluginsService, PluginsService>();
            serviceCollection.AddSingleton<IPluginInitializer, PluginInitializer>();

            return serviceCollection;
        }
    }
}
