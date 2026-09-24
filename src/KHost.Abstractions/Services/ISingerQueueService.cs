using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>The rotation: which singers are in tonight, in what order, and who the console has
/// selected.</summary>
/// <remarks>Each singer's own songs are <see cref="IPerformanceService"/>'s; this orders the singers.
/// A plugin TAKES it — typically to read <see cref="SelectedUserId"/> before enqueuing through
/// <see cref="IPerformanceService.CreateAndEnqueueAsync"/>. The order and selection survive a
/// restart. A singer deleted from the library leaves the queue on their own, with the songs they had
/// waiting.
///
/// <para>A host singleton, callable from any thread; changes never interleave. Every change announces
/// <see cref="KHost.Abstractions.Messaging.Messages.SingerQueueChanged"/>.</para></remarks>
public interface ISingerQueueService
{

    /// <summary>The singers in queue order; the first is up next, or singing.</summary>
    IReadOnlyList<KHostUser> Users { get; }

    /// <summary>The singer the console is acting for — whose songs a pick is queued under — or null.
    /// </summary>
    Guid? SelectedUserId { get; }

    /// <summary>The user behind <see cref="SelectedUserId"/>, or null.</summary>
    KHostUser? SelectedUser { get; }

    /// <summary>True while the first singer is at the microphone, so nobody can be moved above
    /// them nor they moved down.</summary>
    bool IsTopSlotLocked { get; }

    /// <summary>Restores the saved order and selection. Called once by the host at startup.</summary>
    Task InitializeAsync();

    /// <summary>Selects a singer, or no one with null.</summary>
    Task SelectUserAsync(Guid? userId);

    /// <summary>Adds a singer, placed by the venue's rotation rules.</summary>
    Task AddUserAsync(Guid userId);

    /// <summary>Takes a singer out of the queue. Their queued songs stay queued.</summary>
    Task RemoveUserAsync(Guid userId);

    /// <summary>Queues a library search result for a singer in the queue.</summary>
    /// <remarks>Does nothing for a singer not in the queue, or for a result that is not already a
    /// library row: a remote provider's result must be imported first, which is the provider's job.
    /// Goes through <see cref="IPerformanceService.CreateAndEnqueueAsync"/>, so its checks
    /// apply.</remarks>
    Task AddMediaAsync(Guid userId, MediaSearchEntity media);

    /// <summary>Moves a singer one place earlier and selects them; never above a locked top slot.
    /// </summary>
    Task MoveUserUpAsync(Guid userId);

    /// <summary>Moves a singer one place later and selects them; never out of a locked top slot.
    /// </summary>
    Task MoveUserDownAsync(Guid userId);

    /// <summary>Moves a singer to the front. Does nothing while the top slot is locked.</summary>
    Task MoveUserToStartAsync(Guid userId);

    /// <summary>Moves a singer to the back.</summary>
    Task MoveUserToEndAsync(Guid userId);

    /// <summary>Moves a singer to <paramref name="newIndex"/>, 0-based and clamped to the queue.</summary>
    /// <remarks>Refused for index 0 while the top slot is locked.</remarks>
    Task MoveUserToIndexAsync(Guid userId, int newIndex);

    /// <summary>Reorders the queue after a performance per the rotation config (fifo default).</summary>
    /// <remarks>Then selects whoever is first. A rotation rule that fails leaves the order as it was
    /// rather than breaking the queue.</remarks>
    Task RotateQueueAsync(Guid finishedSingerId);

    /// <summary>Selects whoever is first, or no one when the queue is empty.</summary>
    Task SelectFirstUserInQueueAsync();

    /// <summary>Re-reads every singer's details and announces, for after a singer was edited.</summary>
    Task RefreshAsync();

    /// <summary>Empties the queue and deletes every queued song, but only when the venue is set to
    /// clear its queue on close; otherwise does nothing.</summary>
    /// <remarks>Announces nothing.</remarks>
    Task ClearAsync();

    /// <summary>Pins the first singer in place while they sing. Playback calls it on load.</summary>
    /// <remarks>Announces nothing.</remarks>
    void LockTopSlot();

    /// <summary>Releases <see cref="LockTopSlot"/>. Playback calls it when the song ends.</summary>
    void UnlockTopSlot();
}
