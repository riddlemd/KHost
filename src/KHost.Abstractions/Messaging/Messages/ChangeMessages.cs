namespace KHost.Abstractions.Messaging.Messages;

/// <summary>One record per service with something to announce; each carries nothing.</summary>
public sealed record PlaybackChanged;

public sealed record BreakMusicChanged;

/// <summary>Carries <see cref="ProviderSourceName"/> so a stale provider can't redraw the panel.</summary>
public sealed record BreakMusicTrackChanged(string ProviderSourceName);

public sealed record AdsChanged;

public sealed record SingerQueueChanged;

public sealed record PerformancesChanged;

/// <summary>A queued turn gained or lost its render.</summary>
/// <remarks>Its own message rather than PerformancesChanged: the queue itself has not moved, and
/// announcing that it had would re-enter the reconcile that raised this.</remarks>
public sealed record PreparedMediaChanged;

public sealed record ScreensChanged;

public sealed record MediaLibraryChanged;

public sealed record MediaImportChanged;

public sealed record MediaPoolsChanged;

/// <summary>Some venue was added, edited or removed; the list moved.</summary>
public sealed record VenuesChanged;

/// <summary>The venue changed; separate from <see cref="VenuesChanged"/> for the audio baseline.</summary>
public sealed record SelectedVenueChanged;

public sealed record UsersChanged;

public sealed record UserGroupsChanged;

public sealed record TipsChanged;

public sealed record DownloadsChanged;

public sealed record PluginsChanged;

/// <summary>The published list of installable plugins was re-read, from the network or the cache.</summary>
public sealed record PluginCatalogChanged;

/// <summary>An install moved: progress, a settled result, or a staging change.</summary>
public sealed record PluginInstallsChanged;


public sealed record FlashChanged;

public sealed record CastChanged;
