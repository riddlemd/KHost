namespace KHost.Abstractions.Models;

/// <summary>What a playlist is for, not <see cref="MediaType"/>, since purposes span several.</summary>
public enum PoolPurpose
{
    /// <summary>What plays between singers.</summary>
    BreakMusic,

    /// <summary>What plays as a scheduled or host-triggered break.</summary>
    Ads,
}

/// <summary>How the next entry is picked out of a pool.</summary>
public enum PoolSelectionMode
{
    /// <summary>In the order the host arranged them, wrapping at the end.</summary>
    Sequential,

    /// <summary>Even chance across entries, ignoring weight.</summary>
    Shuffle,

    /// <summary>Chance proportional to weight.</summary>
    Weighted,
}

/// <summary>What makes an ad pool come due. Only the active pool's trigger is read.</summary>
public enum AdTriggerMode
{
    /// <summary>Never on its own; the host presses the button.</summary>
    HostOnly,

    /// <summary>Due after a number of performances have played, set by <see cref="MediaPool.AdTriggerInterval"/>.</summary>
    EveryNPerformances,

    /// <summary>Due after a number of minutes have passed, set by <see cref="MediaPool.AdTriggerInterval"/>.</summary>
    EveryNMinutes,

    /// <summary>Whenever the queue runs dry, instead of leaving break music alone out there.</summary>
    OnIdle,
}

/// <summary>A named list to draw from: break music or ads; a media row or a nested pool.</summary>
public class MediaPool : RepositoryModel
{
    /// <summary>What this pool is for.</summary>
    public PoolPurpose Purpose { get; set; } = PoolPurpose.BreakMusic;

    /// <summary>The name shown throughout the app.</summary>
    public required string Name { get; set; }

    /// <summary>Case- and accent-insensitive form of <see cref="Name"/>, used to match it; the host
    /// keeps this in sync, so a plugin should treat it as read-only.</summary>
    public string NameFolded { get; set; } = string.Empty;

    /// <summary>Null belongs to every venue; a value scopes it to one.</summary>
    public Guid? VenueId { get; set; }

    /// <summary>How the next entry is chosen.</summary>
    public PoolSelectionMode SelectionMode { get; set; } = PoolSelectionMode.Shuffle;

    /// <summary>How many recent picks stay ineligible, clamped below the pool's own size.</summary>
    public int NoRepeatCount { get; set; } = 3;

    /// <summary>What makes this pool come due, if it is the active ad pool. Ignored otherwise.</summary>
    public AdTriggerMode AdTrigger { get; set; } = AdTriggerMode.HostOnly;

    /// <summary>The N in <see cref="AdTriggerMode.EveryNPerformances"/> and EveryNMinutes.</summary>
    public int AdTriggerInterval { get; set; } = 4;

    /// <summary>The pool's contents, each either a media row or a nested pool.</summary>
    public List<MediaPoolEntry> Entries { get; set; } = [];
}

/// <summary>One line in a pool: either a media row or a nested pool, never both and never neither.</summary>
/// <remarks>Enforced on save: a pool holding neither is a row the selector would have to skip.</remarks>
public class MediaPoolEntry : RepositoryModel
{
    /// <summary>The pool this entry belongs to.</summary>
    public Guid MediaPoolId { get; set; }

    /// <summary>Order for <see cref="PoolSelectionMode.Sequential"/>; ignored by the other modes.</summary>
    public int Position { get; set; }

    /// <summary>Read only by <see cref="PoolSelectionMode.Weighted"/>. Zero excludes the entry.</summary>
    public int Weight { get; set; } = 1;

    /// <summary>The library row to play, when this entry is not a nested pool.</summary>
    public Guid? MediaId { get; set; }

    /// <summary>Audio to play with this entry; null means whatever the visual brings.</summary>
    public Guid? AudioMediaId { get; set; }

    /// <summary>Where playback starts in the audio; with Duration, trims a clip, no re-encoding.</summary>
    public TimeSpan? AudioStart { get; set; }

    /// <summary>How long the entry runs. Null takes it from the media instead.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>A nested pool to draw from, when this entry is not a single media row.</summary>
    public Guid? ChildPoolId { get; set; }

    /// <summary>Whether this entry is a nested pool rather than a media row.</summary>
    public bool IsPool => ChildPoolId is not null;
}
