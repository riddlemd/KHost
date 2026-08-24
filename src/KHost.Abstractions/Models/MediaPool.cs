namespace KHost.Abstractions.Models;

/// <summary>
/// What a playlist is for. Deliberately not <see cref="MediaType"/>: a kind says what a file is,
/// and both purposes draw on several types — an ad is a picture and a sound together.
/// </summary>
public enum PoolPurpose
{
    BreakMusic,
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
    /// <summary>Never on its own — the host presses the button.</summary>
    HostOnly,
    EveryNPerformances,
    EveryNMinutes,

    /// <summary>Whenever the queue runs dry, instead of leaving break music alone out there.</summary>
    OnIdle,
}

/// <summary>
/// A named list a service draws from: break music between singers, or ads. An entry is either a
/// media row or another pool, so a venue can keep "80s" and "chill" separately and still play
/// across both.
/// </summary>
public class MediaPool : RepositoryModel
{
    public PoolPurpose Purpose { get; set; } = PoolPurpose.BreakMusic;

    public required string Name { get; set; }

    /// <summary>The name as search matches it. Written by the persistence layer, not by hand.</summary>
    public string NameFolded { get; set; } = string.Empty;

    /// <summary>Null belongs to every venue; a value scopes it to one.</summary>
    public Guid? VenueId { get; set; }

    public PoolSelectionMode SelectionMode { get; set; } = PoolSelectionMode.Shuffle;

    /// <summary>
    /// How many recent picks stay ineligible, so a short pool does not repeat itself back to back.
    /// Clamped below the pool's own size at selection time, or nothing would be eligible at all.
    /// </summary>
    public int NoRepeatCount { get; set; } = 3;

    public AdTriggerMode AdTrigger { get; set; } = AdTriggerMode.HostOnly;

    /// <summary>The N in <see cref="AdTriggerMode.EveryNPerformances"/> and EveryNMinutes.</summary>
    public int AdTriggerInterval { get; set; } = 4;

    public List<MediaPoolEntry> Entries { get; set; } = [];
}

/// <summary>
/// One line in a pool: either a media row or a nested pool, never both and never neither. A pool
/// that holds neither is a row the selector has to skip, so the invariant is enforced on save.
/// </summary>
public class MediaPoolEntry : RepositoryModel
{
    public Guid MediaPoolId { get; set; }

    /// <summary>Order for <see cref="PoolSelectionMode.Sequential"/>; ignored by the other modes.</summary>
    public int Position { get; set; }

    /// <summary>Read only by <see cref="PoolSelectionMode.Weighted"/>. Zero excludes the entry.</summary>
    public int Weight { get; set; } = 1;

    public Guid? MediaId { get; set; }

    /// <summary>
    /// Audio to play with this entry. Null means whatever the visual brings: a video's own track,
    /// or silence for a still — and a silent ad leaves break music playing underneath it.
    /// </summary>
    public Guid? AudioMediaId { get; set; }

    /// <summary>
    /// Where in the audio to start. With <see cref="Duration"/> this trims a clip out of a longer
    /// file without re-encoding it — the stream is simply opened at an offset.
    /// </summary>
    public TimeSpan? AudioStart { get; set; }

    /// <summary>How long the entry runs. Null takes it from the media instead.</summary>
    public TimeSpan? Duration { get; set; }

    public Guid? ChildPoolId { get; set; }

    public bool IsPool => ChildPoolId is not null;
}
