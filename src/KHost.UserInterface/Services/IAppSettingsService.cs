using KHost.Abstractions.Models;
using KHost.UserInterface.Models;

namespace KHost.UserInterface.Services;

/// <summary>The machine-level settings the App Settings page edits, as one snapshot.</summary>
public sealed record AppSettings
{
    public bool RequireLogin { get; set; } = true;
    public string? FFmpegPath { get; set; }
    public string? MediaDirectory { get; set; }

    /// <summary>A folder of the host's own song backgrounds, added to the set shipped with the app.
    /// </summary>
    /// <remarks>Machine level rather than per venue: the clips sit on this disk, and a venue only
    /// chooses which of them it wants.</remarks>
    public string? SongBackgroundFolder { get; set; }
    public double StopFadeSeconds { get; set; } = 5;
    public int SegmentSeconds { get; set; } = 2;

    /// <summary>How long an ad runs when its playlist entry and the media itself say nothing.</summary>
    /// <remarks>A still with no voiceover; a video ad runs its own length regardless.</remarks>
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

    /// <summary>Which shape the key, tempo and vocal controls take. Presentation only.</summary>
    /// <remarks>Both shapes drive the same underlying values.</remarks>
    public SongControlStyle SongControlStyle { get; set; } = SongControlStyle.Sliders;

    /// <summary>Opens a screen at startup, for a host running the same room nightly.</summary>
    /// <remarks>Off by default: a machine with no second display puts the screen over the console.</remarks>
    public bool LaunchScreenOnStartup { get; set; }

    /// <summary>The screen launched at startup is named this, so it reclaims its own window.</summary>
    public const string StartupScreenName = "Screen 1";

    /// <summary>The graces the page offers, off first.</summary>
    public static readonly IReadOnlyList<int> LeadInGraceChoices = [0, 5, 10];

    public const double DefaultAdDurationSeconds = 10;
    // A spot has to be long enough to read and short enough that the room does not turn back to
    // its drinks, and the whole point of the setting is that a venue disagrees with the number.
    public const double MinAdDurationSeconds = 1;
    public const double MaxAdDurationSeconds = 300;

    public const int DefaultPageSize = 25;
    // Lower than a full page's: this list lives in a dialog whose table is capped at 500px.
    public const int DefaultPerformanceHistoryPageSize = 10;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 500;
}

public interface IAppSettingsService
{
    /// <summary>The effective values: deployment defaults with the overlay applied.</summary>
    AppSettings Current { get; }

    /// <summary>A change to a startup-only setting was saved and waits for a restart.</summary>
    bool RestartRequired { get; }

    /// <summary>The directory used in place of a blank <see cref="AppSettings.MediaDirectory"/>.</summary>
    string DefaultMediaDirectory { get; }

    /// <summary>Writes the overlay; turning login on is refused while no admin has a password.</summary>
    /// <remarks>That would lock every operator out.</remarks>
    Task<AppSettingsSaveResult> SaveAsync(AppSettings settings);
}

public sealed record AppSettingsSaveResult(bool Saved, string? Error = null);
