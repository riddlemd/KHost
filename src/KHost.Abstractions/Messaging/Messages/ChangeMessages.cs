namespace KHost.Abstractions.Messaging.Messages;

/// <summary>One record per service with something to announce; each carries nothing.</summary>
public sealed record PlaybackChanged;

/// <summary>What is playing between singers moved: a new track, a pause, or a stop.</summary>
public sealed record BreakMusicChanged;

/// <summary>The break-music provider's own reported track changed.</summary>
/// <param name="ProviderSourceName">The reporting provider's display name.</param>
/// <remarks>Carried so a panel drawn for one provider does not redraw itself from another's
/// message after the host has switched providers.</remarks>
public sealed record BreakMusicTrackChanged(string ProviderSourceName);

/// <summary>An ad, ad pool or ad schedule changed; re-read whichever of those matters to a listener.</summary>
public sealed record AdsChanged;

/// <summary>The singer queue's order or contents changed.</summary>
public sealed record SingerQueueChanged;

/// <summary>A performance was recorded, edited or removed.</summary>
public sealed record PerformancesChanged;

/// <summary>The media library's rows changed: an add, edit, delete or status change.</summary>
public sealed record MediaLibraryChanged;

/// <summary>An import's progress or state changed; the Downloads and import pages should re-read.</summary>
public sealed record MediaImportChanged;

/// <summary>A media pool (break music or ads) or its entries changed.</summary>
public sealed record MediaPoolsChanged;

/// <summary>Some venue was added, edited or removed; the list moved.</summary>
public sealed record VenuesChanged;

/// <summary>The venue changed; separate from <see cref="VenuesChanged"/> for the audio baseline.</summary>
public sealed record SelectedVenueChanged;

/// <summary>A user account was added, edited or removed.</summary>
public sealed record UsersChanged;

/// <summary>A user group or its permissions changed.</summary>
public sealed record UserGroupsChanged;

/// <summary>A tip was recorded or edited.</summary>
public sealed record TipsChanged;

/// <summary>A download's progress or state changed; the Downloads page should re-read.</summary>
public sealed record DownloadsChanged;

/// <summary>An installed plugin's state changed: loaded, unloaded, enabled or disabled.</summary>
public sealed record PluginsChanged;

/// <summary>The published list of installable plugins was re-read, from the network or the cache.</summary>
public sealed record PluginCatalogChanged;

/// <summary>An install moved: progress, a settled result, or a staging change.</summary>
public sealed record PluginInstallsChanged;

/// <summary>The message banner across the top of the console changed: a new message or withdrawn.</summary>
public sealed record FlashChanged;

/// <summary>The connected display, or what it can do, changed.</summary>
public sealed record DisplaysChanged;

/// <summary>A plugin's table moved and an open dialog should re-read it.</summary>
/// <remarks>Deliberately not per-plugin: the dialog shows one table at a time, and a re-read is
/// cheap enough that telling them apart would buy nothing.</remarks>
public sealed record PluginTableChanged;
