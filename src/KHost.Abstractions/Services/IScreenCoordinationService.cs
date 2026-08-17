using KHost.Abstractions.Services.IPC;

namespace KHost.Abstractions.Services;

/// <summary>
/// Assigns the two roles that cannot be inferred from a screen's capabilities: which screen the
/// room hears, and which screen's position the others are held to.
/// </summary>
/// <remarks>
/// These were one role until a Cast device made them separable. A Cast receiver can carry the
/// room's audio but can never be held to anyone's schedule, so the screen that is audible and the
/// screen that anchors the timeline are no longer necessarily the same one.
/// </remarks>
public interface IScreenCoordinationService
{
    event EventHandler? StateChanged;

    /// <summary>
    /// Starts watching for screens. Must run at startup: screens register as soon as the hub is
    /// mapped, and a service nobody has resolved yet cannot mute the first one that arrives.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The screen the room hears. Any audio-capable screen can hold this, including one that
    /// cannot sync — a Cast device driving the TV is the obvious case.
    /// </summary>
    string? AudioScreenId { get; }

    /// <summary>
    /// The screen whose reported position anchors the group timeline. Derived, not chosen: it is
    /// <see cref="AudioScreenId"/> whenever that screen can sync, because a screen being corrected
    /// is a screen being seeked, and seeking the one the room hears is an audible glitch.
    /// Falls back to any sync-capable screen when the audio is somewhere unsyncable.
    /// </summary>
    string? TimingScreenId { get; }

    /// <summary>
    /// True when audio and timing have landed on different screens — the lyrics displays are then
    /// following a clock that is not what the room hears, and can drift away from it.
    /// </summary>
    bool RolesAreSplit { get; }

    /// <summary>Fills both roles if they are vacant. Returns the screen the room hears.</summary>
    Task<string?> EnsureRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the room's audio to a specific screen, re-deriving the timing reference to match.
    /// Refused for a screen that renders no audio.
    /// </summary>
    Task<bool> SetAudioScreenAsync(string screenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the screen is currently audible. Defaults to "only the audio screen", so adding a
    /// screen never silently adds a second voice to the room.
    /// </summary>
    bool IsAudioEnabled(string screenId);

    /// <summary>Overrides the mute for one screen, and pushes the change to it.</summary>
    Task SetAudioEnabledAsync(string screenId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Clears an override, returning the screen to following the audio role.</summary>
    Task ClearAudioOverrideAsync(string screenId, CancellationToken cancellationToken = default);

    /// <summary>True when the user has pinned this screen's audio rather than letting it follow.</summary>
    bool HasAudioOverride(string screenId);
}
