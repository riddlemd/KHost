using KHost.Abstractions.Models;
using KHost.Domain.Services;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.Domain.Services.MediaProviders;
using KHost.Domain.Services.VideoEncoding;
using KHost.UserInterface.Models;

namespace KHost.UserInterface.Services;

/// <summary>The machine-level settings the App Settings page edits, as one snapshot.</summary>
public sealed record AppSettings
{
    /// <summary>Read-only here: it comes from <c>Auth:RequireLogin</c> in configuration, not from a
    /// save through this page — see <see cref="IAppSettingsService.SaveAsync"/>.</summary>
    public bool RequireLogin { get; init; }
    public string? FFmpegPath { get; set; }
    public string? MediaDirectory { get; set; }

    public double StopFadeSeconds { get; set; } = 5;
    public int SegmentSeconds { get; set; } = 2;

    /// <summary>The frame height a CD+G is scaled into; one of <see cref="GraphicsScaling.Heights"/>,
    /// <see cref="GraphicsScaling.Off"/> for its native size.</summary>
    public int GraphicsScaleHeight { get; set; } = GraphicsScaling.DefaultHeight;

    /// <summary>Whether songs are encoded on the graphics chip, on the processor, or on the chip
    /// when this machine has one that works.</summary>
    public VideoEncoderPreference VideoEncoder { get; set; } = VideoEncoderPreference.Auto;

    /// <summary>How long an ad runs when its playlist entry and the media itself say nothing.</summary>
    /// <remarks>A still with no voiceover; a video ad runs its own length regardless. One of
    /// <see cref="AdDurationChoices"/>.</remarks>
    public double AdDefaultDurationSeconds { get; set; } = DefaultAdDurationSeconds;
    public int MediaPageSize { get; set; } = DefaultPageSize;
    public int UsersPageSize { get; set; } = DefaultPageSize;
    public int UserGroupsPageSize { get; set; } = DefaultPageSize;
    public int TipsPageSize { get; set; } = DefaultPageSize;
    public int VenuesPageSize { get; set; } = DefaultPageSize;
    public int PerformanceHistoryPageSize { get; set; } = DefaultPerformanceHistoryPageSize;

    /// <summary>Where the backing voices start on an unmixed multi-track song.</summary>
    /// <remarks>The lead vocal has none; a singer is there to replace it and starts silent.</remarks>
    public int BackingVocalVolume { get; set; } = AudioMix.DefaultBackingVolume;

    /// <summary>The least run-up, in seconds, before the first words of a song whose words the
    /// screen draws; zero for none.</summary>
    /// <remarks>One of <see cref="LeadInGraceChoices"/>.</remarks>
    public int LeadInGraceSeconds { get; set; }

    /// <summary>Whether the host adds a lead-in to a line the timed lyrics left without one.</summary>
    /// <remarks>Off by default: it changes how every provider's songs look.</remarks>
    public bool DynamicLeadIns { get; set; }

    /// <summary>The silence before a line, in seconds, that earns it a host-added lead-in.</summary>
    /// <remarks>One of <see cref="DynamicLeadInPauseChoices"/>.</remarks>
    public int DynamicLeadInPauseSeconds { get; set; } = LeadInGenerator.DefaultLongPauseSeconds;

    /// <summary>Whether the host moves apart timed-lyric colours a colour-blind viewer would confuse.</summary>
    /// <remarks>Off by default: it changes how some songs' colours look to everyone.</remarks>
    public bool ColorBlindFriendlyLyrics { get; set; }

    /// <summary>Which shape the key, tempo and vocal controls take. Presentation only.</summary>
    /// <remarks>Both shapes drive the same underlying values.</remarks>
    public SongControlStyle SongControlStyle { get; set; } = SongControlStyle.Sliders;

    /// <summary>Opens a screen at startup, for a host running the same room nightly.</summary>
    /// <remarks>Off by default: a machine with no second display puts the screen over the console.</remarks>
    public bool LaunchScreenOnStartup { get; set; }

    /// <summary>Which mode Song Search starts in: a loaded provider's <c>SourceName</c> (Local
    /// included, via <see cref="LocalSearchMode"/>), or <see cref="RememberLastSearchMode"/> to
    /// start in whatever mode was last picked.</summary>
    /// <remarks>A mode naming a provider no longer loaded falls back to Local — the search panel's
    /// call, since only it knows which providers are loaded right now.</remarks>
    public string DefaultSearchMode { get; set; } = LocalSearchMode;

    /// <summary>The break music provider every venue plays from, by its <c>SourceName</c>.</summary>
    /// <remarks>Until saved here it reads as whichever provider is active, so the first save
    /// keeps what the room already hears.</remarks>
    public string? BreakMusicProvider { get; set; }

    /// <summary>The shipped visualisation playlist a venue starts on when it is added.</summary>
    /// <remarks>A venue already made keeps the playlist it names.</remarks>
    public VenueBackgrounds NewVenueBackgrounds { get; set; } = VenueBackgrounds.Basic;

    /// <summary>The library image a venue shows while nothing plays when it is added; null leaves
    /// its screen blank.</summary>
    /// <remarks>A venue already made keeps its own.</remarks>
    public Guid? NewVenuePlaceholderImageId { get; set; }

    /// <summary>How that image fills a new venue's screen; null takes the image's own answer.</summary>
    public ImageScaling? NewVenuePlaceholderImageScaling { get; set; }

    /// <summary>The screen launched at startup is named this, so it reclaims its own window.</summary>
    public const string StartupScreenName = "Screen 1";

    /// <summary>The <see cref="DefaultSearchMode"/> value meaning "always start in the local
    /// library" — today's behaviour.</summary>
    /// <remarks>Equal to <see cref="LocalMediaProvider"/>'s own SourceName, so the search panel
    /// treats a configured default exactly like any other provider pick.</remarks>
    public const string LocalSearchMode = nameof(LocalMediaProvider);

    /// <summary>The <see cref="DefaultSearchMode"/> value meaning "start in whatever mode was last
    /// picked", persisted per machine rather than per venue.</summary>
    public const string RememberLastSearchMode = "Remember";

    /// <summary>The graces the page offers, off first, ending at the longest the screen honours.</summary>
    public static readonly IReadOnlyList<int> LeadInGraceChoices = [0, 5, (int)LeadInGrace.MaxSeconds];

    /// <summary>The pauses the page offers, shortest first.</summary>
    public static readonly IReadOnlyList<int> DynamicLeadInPauseChoices = [1, 2, 3, 4, 5];

    public const double DefaultAdDurationSeconds = 10;

    /// <summary>The default durations the page offers, shortest first.</summary>
    public static readonly IReadOnlyList<double> AdDurationChoices = [5, 10, 15, 20, 25, 30];

    // An ad entry's own duration: long enough to read and short enough that the room does not
    // turn back to its drinks, and the whole point of the setting is that a venue disagrees.
    public const double MinAdDurationSeconds = 1;
    public const double MaxAdDurationSeconds = 300;

    public const int DefaultPageSize = 25;
    // Lower than a full page's: this list lives in a dialog whose table is capped at 500px.
    public const int DefaultPerformanceHistoryPageSize = 10;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 500;

    /// <summary>The fades the page offers, shortest first; none is no fade at all.</summary>
    /// <remarks>The stop waits out the whole fade before the queue moves on, so a long one is dead air.</remarks>
    public static readonly IReadOnlyList<double> StopFadeChoices = [0, 1, 2, 3, 4, 5, 6, 8, 10, 15, 20, 30];

    // Below one second is no segment at all; past ten, a seek or a key change waits a whole
    // segment before the screen has anything to play.
    public const int MinSegmentSeconds = 1;
    public const int MaxSegmentSeconds = 10;
}

public interface IAppSettingsService
{
    /// <summary>The effective values: deployment defaults with the overlay applied.</summary>
    AppSettings Current { get; }

    /// <summary>A change to a startup-only setting was saved and waits for a restart.</summary>
    bool RestartRequired { get; }

    /// <summary>The directory used in place of a blank <see cref="AppSettings.MediaDirectory"/>.</summary>
    string DefaultMediaDirectory { get; }

    /// <summary>Writes the overlay. Does not touch <see cref="AppSettings.RequireLogin"/> — that
    /// is a configuration-only flag, not something this page saves.</summary>
    Task<AppSettingsSaveResult> SaveAsync(AppSettings settings);

    /// <summary>Saves <see cref="AppSettings.NewVenueBackgrounds"/> alone, keeping every other key in
    /// the overlay as it is.</summary>
    Task SaveNewVenueBackgroundsAsync(VenueBackgrounds backgrounds);

    /// <summary>Saves <see cref="AppSettings.NewVenuePlaceholderImageId"/> and
    /// <see cref="AppSettings.NewVenuePlaceholderImageScaling"/> alone, as
    /// <see cref="SaveNewVenueBackgroundsAsync"/> does.</summary>
    Task SaveNewVenuePlaceholderImageAsync(Guid? mediaId, ImageScaling? scaling);
}

public sealed record AppSettingsSaveResult(bool Saved, string? Error = null);
