using KHost.Abstractions.Services.IPC;

namespace KHost.Abstractions.Services;

/// <summary>
/// Owns which screen the room hears and which the rest follow. Two screens playing the same song
/// a few milliseconds apart is comb filtering, so exactly one is audible unless someone says
/// otherwise — and the audible one is the primary, because the primary is never rate-corrected.
/// </summary>
public interface IScreenCoordinationService
{
    event EventHandler? StateChanged;

    /// <summary>
    /// Starts watching for screens. Must run at startup: screens register as soon as the hub is
    /// mapped, and a service nobody has resolved yet cannot mute the first one that arrives.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>The screen defining the group timeline, or null when no synced screen is present.</summary>
    string? PrimaryScreenId { get; }

    /// <summary>
    /// Elects a primary if there is none, preferring a screen that renders audio. Returns null
    /// when nothing in the synced group can take the role.
    /// </summary>
    Task<string?> EnsurePrimaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Hands the role to a specific screen. Only a sync-capable screen can take it — a loose
    /// consumer cannot be held to the timeline it would be defining.
    /// </summary>
    Task<bool> SetPrimaryAsync(string screenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the screen is currently audible. Defaults to "only the primary", so adding a screen
    /// never silently adds a second voice to the room.
    /// </summary>
    bool IsAudioEnabled(string screenId);

    /// <summary>Overrides the mute for one screen, and pushes the change to it.</summary>
    Task SetAudioEnabledAsync(string screenId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Clears an override, returning the screen to following the primary.</summary>
    Task ClearAudioOverrideAsync(string screenId, CancellationToken cancellationToken = default);

    /// <summary>True when the user has pinned this screen's audio rather than letting it follow.</summary>
    bool HasAudioOverride(string screenId);
}
