namespace KHost.UserInterface.Services;

/// <summary>The machine-level settings the App Settings page edits, as one snapshot.</summary>
public sealed class AppSettings
{
    public bool RequireLogin { get; set; } = true;
    public string? FFmpegPath { get; set; }
    public double StopFadeSeconds { get; set; } = 5;
    public double SyncStartLeadMilliseconds { get; set; } = 400;
    public int SegmentSeconds { get; set; } = 2;
    public int MediaPageSize { get; set; } = DefaultPageSize;
    public int UsersPageSize { get; set; } = DefaultPageSize;
    public int UserGroupsPageSize { get; set; } = DefaultPageSize;
    public int TipsPageSize { get; set; } = DefaultPageSize;
    public int VenuesPageSize { get; set; } = DefaultPageSize;
    public int PerformanceHistoryPageSize { get; set; } = DefaultPerformanceHistoryPageSize;

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

    /// <summary>
    /// Writes the overlay. Turning the login requirement on is refused while no admin-group
    /// user has a password — that would lock every operator out of the console.
    /// </summary>
    Task<AppSettingsSaveResult> SaveAsync(AppSettings settings);
}

public sealed record AppSettingsSaveResult(bool Saved, string? Error = null);
