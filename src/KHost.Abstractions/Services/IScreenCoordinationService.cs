using KHost.Abstractions.Services.IPC;

namespace KHost.Abstractions.Services;

/// <summary>Which screen the room hears, and which sync to it; a loose consumer can't hold sync.</summary>
public interface IScreenCoordinationService
{

    /// <summary>Must run at startup, or nobody mutes the first screen to arrive.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>The screen the room hears. Does not require sync; a loose consumer can hold it.</summary>
    string? AudioScreenId { get; }

    /// <summary>Whose position the others sync to: the audio screen, whenever it can sync.</summary>
    string? PrimaryScreenId { get; }

    /// <summary>True when the two land on different screens, so followers can drift from the room.</summary>
    bool RolesAreSplit { get; }

    /// <summary>Fills both roles if vacant. Returns the screen the room hears.</summary>
    Task<string?> EnsureRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>Moves the audio, re-deriving the primary. Refused for a screen that renders none.</summary>
    Task<bool> SetAudioScreenAsync(string screenId, CancellationToken cancellationToken = default);

    /// <summary>Defaults to the audio screen alone, so adding a screen never adds a second voice.</summary>
    bool IsAudioEnabled(string screenId);

    Task SetAudioEnabledAsync(string screenId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Drops an override so the screen follows the audio role again.</summary>
    Task ClearAudioOverrideAsync(string screenId, CancellationToken cancellationToken = default);

    bool HasAudioOverride(string screenId);

    /// <summary>Whether the screen renders a picture; blanking doesn't take it off the timeline.</summary>
    bool IsVideoEnabled(string screenId);

    Task SetVideoEnabledAsync(string screenId, bool enabled, CancellationToken cancellationToken = default);
}
