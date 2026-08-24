namespace KHost.Plugins.Sdk.Messaging.Messages;

/// <summary>
/// One record per service that used to raise <c>StateChanged</c>. They carry nothing: each says
/// only that its corner of the show moved, which is all the event ever said — but a subscriber now
/// names the corner it cares about instead of taking every service's word for it.
/// </summary>
public sealed record PlaybackChanged;

public sealed record BreakMusicChanged;

public sealed record AdsChanged;

public sealed record SingerQueueChanged;

public sealed record PerformancesChanged;

public sealed record ScreensChanged;

public sealed record MediaLibraryChanged;

public sealed record MediaImportChanged;

public sealed record MediaPoolsChanged;

public sealed record VenuesChanged;

public sealed record UsersChanged;

public sealed record UserGroupsChanged;

public sealed record TipsChanged;

public sealed record DownloadsChanged;

public sealed record PluginsChanged;

public sealed record PluginLibraryChanged;

public sealed record FlashChanged;

public sealed record CastChanged;
