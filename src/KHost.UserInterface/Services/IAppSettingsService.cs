using KHost.Abstractions.Models;

namespace KHost.UserInterface.Services;

/// <summary>The machine-level settings the App Settings page edits, as one snapshot.</summary>
public sealed class AppSettings
{
    public bool RequireLogin { get; set; } = true;
    public string? FFmpegPath { get; set; }
    public string? MediaDirectory { get; set; }
    public double StopFadeSeconds { get; set; } = 5;
    public double SyncStartLeadMilliseconds { get; set; } = 400;
    public int SegmentSeconds { get; set; } = 2;

    /// <summary>
    /// How long an ad runs when its playlist entry does not say and the media cannot answer — a
    /// still with no voiceover. A video ad runs for its own length regardless.
    /// </summary>
    public double AdDefaultDurationSeconds { get; set; } = DefaultAdDurationSeconds;
    public int MediaPageSize { get; set; } = DefaultPageSize;
    public int UsersPageSize { get; set; } = DefaultPageSize;
    public int UserGroupsPageSize { get; set; } = DefaultPageSize;
    public int TipsPageSize { get; set; } = DefaultPageSize;
    public int VenuesPageSize { get; set; } = DefaultPageSize;
    public int PerformanceHistoryPageSize { get; set; } = DefaultPerformanceHistoryPageSize;

    /// <summary>
    /// Where the backing voices start on a multi-track song nobody has mixed by hand. The lead
    /// vocal has no setting: a singer is there to replace it, so it always starts silent.
    /// </summary>
    public int BackingVocalVolume { get; set; } = AudioMix.DefaultBackingVolume;

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
    /// <summary>The effective values — deployment defaults with the overlay applied.</summary>
    AppSettings Current { get; }

    /// <summary>A change to a startup-only setting was saved and waits for a restart.</summary>
    bool RestartRequired { get; }

    /// <summary>The directory used in place of a blank <see cref="AppSettings.MediaDirectory"/>, for display.</summary>
    string DefaultMediaDirectory { get; }

    /// <summary>
    /// Writes the overlay. Turning the login requirement on is refused while no admin-group
    /// user has a password — that would lock every operator out of the console.
    /// </summary>
    Task<AppSettingsSaveResult> SaveAsync(AppSettings settings);
}

public sealed record AppSettingsSaveResult(bool Saved, string? Error = null);
