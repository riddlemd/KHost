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

/// <summary>The flash stack changed: a message was shown or one was withdrawn.</summary>
public sealed record FlashChanged;

/// <summary>The connected display, or what it can do, changed.</summary>
public sealed record DisplaysChanged;

/// <summary>A plugin's table moved and an open dialog should re-read it.</summary>
/// <remarks>Deliberately not per-plugin: the dialog shows one table at a time, and a re-read is
/// cheap enough that telling them apart would buy nothing.</remarks>
public sealed record PluginTableChanged;

/// <summary>The QR code a venue shows may have moved: an owner offered or withdrew one.</summary>
/// <remarks>Read <see cref="KHost.Abstractions.Services.IQrCodeOfferService.ReadOfferAsync"/> again
/// on it. A venue edit that moves the code announces
/// <see cref="SelectedVenueChanged"/> instead, and a song starting or ending
/// <see cref="PlaybackChanged"/>, so a display drawing the code listens to all three. Published and
/// awaited: the owner that offered the code returns once every display has been told.</remarks>
public sealed record QrCodeOfferChanged;

/// <summary>Who sings next, or what they sing, may have moved.</summary>
/// <remarks>Read <see cref="KHost.Abstractions.Services.IUpNextService.ReadAsync"/> again on it.
/// Announced once for one host action, however many steps it took, and not for a change that
/// cannot move the list — a pause, a seek, a venue edit that leaves its alias rule alone. It may
/// still arrive when the list reads the same afterwards.</remarks>
public sealed record UpNextChanged;
