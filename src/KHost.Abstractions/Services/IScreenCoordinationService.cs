using KHost.Abstractions.Services.IPC;

namespace KHost.Abstractions.Services;

/// <summary>
/// Assigns which screen the room hears and which screen the others are held to. One role until a
/// Cast device made them separable: a receiver can carry audio but can never hold a schedule.
/// </summary>
public interface IScreenCoordinationService
{

    /// <summary>
    /// Must run at startup: screens register as soon as the hub is mapped, and a service nobody
    /// has resolved yet cannot mute the first one to arrive.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>The screen the room hears. Does not require sync — a Cast device can hold it.</summary>
    string? AudioScreenId { get; }

    /// <summary>
    /// Whose position the others are held to. Derived: the audio screen whenever it can sync,
    /// because correcting a screen means seeking it and seeking the audible one is a glitch.
    /// </summary>
    string? PrimaryScreenId { get; }

    /// <summary>True when the two landed on different screens, so the followers can drift from the room.</summary>
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

    /// <summary>
    /// Whether the screen is rendering a picture. On by default for anything that can; blanking
    /// one does not take it off the timeline, so it stays in step and can be turned back on.
    /// </summary>
    bool IsVideoEnabled(string screenId);

    Task SetVideoEnabledAsync(string screenId, bool enabled, CancellationToken cancellationToken = default);
}
