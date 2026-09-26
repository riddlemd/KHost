using KHost.Abstractions.Messaging;
using KHost.Abstractions.Services;
using KHost.IPC.SignalR.Contracts;
using KHost.Domain.Services;
using KHost.Domain.Services.BurnIn;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.AuthProviders;
using KHost.Domain.Services.Ads;
using KHost.Domain.Services.BreakMusic;
using KHost.Domain.Services.Displays;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.Domain.Services.QrCodes;
using KHost.Domain.Services.MediaPools;
using KHost.Domain.Services.MediaProviders;
using KHost.Abstractions.Services.QueueRotation;
using KHost.Domain.Services.PasswordHashers;
using KHost.Domain.Services.Plugins;
using KHost.Domain.Services.QueueRotation;
using KHost.Domain.Services.QueueRotation.Modes;
using KHost.LrcLib;
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

            serviceCollection.AddSingleton<ResolvedHostAddress>();

            serviceCollection.AddOptions<LocalScreenProvider.ServiceOptions>()
                .BindConfiguration(LocalScreenProvider.ServiceOptions.SectionName)
                .PostConfigure<ResolvedHostAddress>((options, resolved) =>
                {
                    if (resolved.ScreenIpcUri is { Length: > 0 } uri)
                        options.ServerUri = uri;
                });

            serviceCollection.AddOptions<PlaybackService.ServiceOptions>()
                .BindConfiguration(PlaybackService.ServiceOptions.SectionName);

            serviceCollection.AddOptions<HlsMediaStreamService.ServiceOptions>()
                .BindConfiguration(HlsMediaStreamService.ServiceOptions.SectionName)
                .PostConfigure<ResolvedHostAddress>((options, resolved) =>
                {
                    if (resolved.MediaStreamBaseAddress is { Length: > 0 } address)
                        options.BaseAddress = address;
                });

            serviceCollection.AddOptions<MediaAcquisitionService.ServiceOptions>()
                .BindConfiguration(MediaAcquisitionService.ServiceOptions.SectionName);

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
            serviceCollection.AddSingleton<HlsMediaStreamService>();
            serviceCollection.AddSingleton<IMediaStreamService>(services => services.GetRequiredService<HlsMediaStreamService>());
            // The same instance, so a burned-in stream is one of its sessions and closes like any other.
            serviceCollection.AddSingleton<IBurnInStreamService>(services => services.GetRequiredService<HlsMediaStreamService>());
            serviceCollection.AddSingleton<LyricBurnIn>();
            // Keyed, so it never appears in the IMediaProbe enumerable the plugin probes arrive
            // through: it claims every file and would answer ahead of whoever owns the format.
            serviceCollection.AddKeyedSingleton<IMediaProbe, FfprobeMediaProbe>(MediaProbeService.FallbackKey);
            // A CD+G's facts live in the audio beside it, so it describes itself rather than
            // having the parsing service redirect the probe on its behalf.
            serviceCollection.AddSingleton<IMediaProbe, CdgMediaProbe>();
            serviceCollection.AddSingleton<IMediaProbeService, MediaProbeService>();
            serviceCollection.AddSingleton<IMediaRenderer, CompactDiscPlusGraphicsRenderer>();
            // Keyed for the same reason as the probe fallback: it claims every file, so it must not
            // race the renderers that claim one.
            serviceCollection.AddKeyedSingleton<IMediaRenderer, StreamingMediaRenderer>(MediaRendererService.FallbackKey);
            serviceCollection.AddSingleton<IMediaRendererService, MediaRendererService>();
            serviceCollection.AddSingleton<IPlayableMediaSourceService, PlayableMediaSourceService>();
            serviceCollection.AddSingleton<ITimedLyricsService, TimedLyricsService>();
            serviceCollection.AddSingleton<IAudioTrackService, AudioTrackService>();
            serviceCollection.AddSingleton<IMediaTagReader, MediaTagReader>();
            serviceCollection.AddSingleton<IMediaGateService, MediaGateService>();

            // Core, not a plugin: the host's own transport to the local screen app, registered here so it
            // reaches PlaybackService in the same collection a plugin's display does. PluginLoader
            // must never bind it, and it must not appear on the Plugins page.
            serviceCollection.AddSingleton<LocalScreenDisplayProvider>();
            serviceCollection.AddSingleton<IDisplayProvider>(
                provider => provider.GetRequiredService<LocalScreenDisplayProvider>());

            // It wires ScreenConnected in its constructor, so it must exist before a screen does.
            serviceCollection.AddSingleton<IStartsWithTheHost>(
                provider => provider.GetRequiredService<LocalScreenDisplayProvider>());
            serviceCollection.AddSingleton<INextSingerCardService, NextSingerCardService>();
            serviceCollection.AddSingleton<IQrCodeService, QrCodeService>();

            // The same singleton, so a reader sees what the registry holds; only the read side is public.
            serviceCollection.AddSingleton<IQrCodeOfferService>(sp => sp.GetRequiredService<IQrCodeService>());
            serviceCollection.AddSingleton<IUpNextService, UpNextService>();

            // It subscribes in its constructor, so it must exist before anything announces.
            serviceCollection.AddSingleton<IStartsWithTheHost>(
                sp => (IStartsWithTheHost)sp.GetRequiredService<IUpNextService>());
            serviceCollection.AddSingleton<IPlaybackService, PlaybackService>();

            // The same singletons again, under the marker the host builds on the way up: pointed at what is
            // already registered. A fresh registration would build twice, and the extra copy would listen.
            serviceCollection.AddSingleton<IStartsWithTheHost>(
                sp => (IStartsWithTheHost)sp.GetRequiredService<IPlaybackService>());
            serviceCollection.AddSingleton<IMediaSearchService, MediaSearchService>();
            serviceCollection.AddSingleton<IVenuesService, VenuesService>();
            serviceCollection.AddSingleton<IUsersService, UsersService>();
            serviceCollection.AddSingleton<IPerformanceService, PerformanceService>();
            serviceCollection.AddSingleton<IMediaService, MediaService>();
            serviceCollection.AddSingleton<IMediaPoolService, MediaPoolService>();
            serviceCollection.AddSingleton<IBreakMusicProvider, LibraryBreakMusicProvider>();
            serviceCollection.AddSingleton<IBreakMusicService, BreakMusicService>();
            serviceCollection.AddSingleton<IAdService, AdService>();
            serviceCollection.AddOptions<BackgroundPackService.ServiceOptions>()
                .BindConfiguration(BackgroundPackService.ServiceOptions.SectionName);
            serviceCollection.AddSingleton<IBackgroundPackService, BackgroundPackService>();
            serviceCollection.AddSingleton<ILyricsService, LyricsService>();
            serviceCollection.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
            serviceCollection.AddSingleton<IAuthService, AuthService>();
            serviceCollection.AddSingleton<IAuthProvider, LocalAuthProvider>();
            serviceCollection.AddSingleton<IUserGroupsService, UserGroupsService>();
            serviceCollection.AddSingleton<ITipsService, TipsService>();

            serviceCollection.AddSingleton<IScreenProvider, LocalScreenProvider>();
            serviceCollection.AddSingleton<IScreenKeyStore, FileScreenKeyStore>();

            serviceCollection.AddSingleton<IMediaProvider, LocalMediaProvider>();
            // Registered ahead of MediaAcquisitionService: it depends on IPerformanceService, which
            // depends on IDownloadsService, so this must stay its own dependency-free singleton.
            serviceCollection.AddSingleton<IDownloadsService, DownloadsService>();
            serviceCollection.AddSingleton<IPluginStagingArea>(new PluginStagingArea(PluginPaths.Plugins, PluginPaths.Staging));
            // Chosen for the machine, honest when it has nowhere safe: a venue that cannot protect a
            // secret is asked instead. Registered here since a plugin context builds during discovery.
            serviceCollection.AddSingleton(_ => KHost.Secrets.SecretStores.ForThisMachine());

            serviceCollection.AddSingleton<Services.Plugins.Secrets.IPluginSecretStore,
                Services.Plugins.Secrets.PluginSecretStore>();

            serviceCollection.AddSingleton<IPluginPayloadReader, PluginPayloadReader>();
            serviceCollection.AddSingleton<IPluginCatalogService, PluginCatalogService>();
            serviceCollection.AddSingleton<IPluginInstallerService, PluginInstallerService>();
            serviceCollection.AddSingleton<MediaAcquisitionService>();
            serviceCollection.AddSingleton<IMediaAcquisitionService>(sp => sp.GetRequiredService<MediaAcquisitionService>());

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

        /// <summary>Discovers and loads enabled plugins; must run before the container is built.</summary>
        public static IServiceCollection AddPlugins(this IServiceCollection serviceCollection)
        {
            // Before Discover, so a payload staged by the last run is a plugin this one can see. Its own
            // instance: the singleton does not exist yet, and staging keeps no state outside the folder.
            new PluginStagingArea(PluginPaths.Plugins, PluginPaths.Staging).ApplyPending();

            var state = PluginLoader.ReadState(PluginPaths.Cache);
            var plugins = PluginLoader.Discover(PluginPaths.Plugins, state);

            PluginLoader.LoadAndRegister(serviceCollection, plugins, state);

            serviceCollection.AddSingleton<IPluginRegistry>(new PluginRegistry(plugins));
            serviceCollection.AddSingleton<IPluginsService, PluginsService>();
            serviceCollection.AddSingleton<IPluginButtonService, PluginButtonService>();
            serviceCollection.AddSingleton<IPluginInitializer, PluginInitializer>();

            return serviceCollection;
        }
    }
}
