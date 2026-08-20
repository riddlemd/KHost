namespace KHost.UserInterface.Services;

/// <summary>The machine-level settings the App Settings page edits, as one snapshot.</summary>
public sealed class AppSettings
{
    public bool RequireLogin { get; set; } = true;
    public string? FFmpegPath { get; set; }
    public double StopFadeSeconds { get; set; } = 5;
    public double SyncStartLeadMilliseconds { get; set; } = 400;
    public int SegmentSeconds { get; set; } = 2;
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
