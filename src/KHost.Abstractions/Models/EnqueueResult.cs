namespace KHost.Abstractions.Models;

/// <summary>Whether a song reached the queue, and which rule stopped it when it did not.</summary>
public enum EnqueueResultType
{
    /// <summary>Saved at the end of the singer's list.</summary>
    Queued,

    /// <summary>The singer already has this song waiting. Refused without telling the host.</summary>
    AlreadyQueued,

    /// <summary>Another singer has this song waiting and the venue refuses such a song
    /// (<see cref="Venue.VenueSettings.RefuseSongQueuedForAnotherSinger"/>). Refused without telling
    /// the host.</summary>
    QueuedForAnotherSinger,

    /// <summary>The venue's duplicate-song warning was shown and the host declined it.</summary>
    DeclinedAtWarning,

    /// <summary>The gate that owns the media refused it for <see cref="Services.MediaAction.Queue"/>.
    /// </summary>
    RefusedByProvider,
}

/// <summary>What became of a request to put a song on a singer's list.</summary>
/// <param name="Type">Whether it was queued, and if not, why.</param>
/// <param name="Performance">The saved performance; set only when <paramref name="Type"/> is
/// <see cref="EnqueueResultType.Queued"/>.</param>
/// <param name="Reason">The refusal the host was shown; set only when <paramref name="Type"/> is
/// <see cref="EnqueueResultType.RefusedByProvider"/>.</param>
/// <param name="Conflict">The queued performance that stood in the way; set only when
/// <paramref name="Type"/> is <see cref="EnqueueResultType.AlreadyQueued"/> or
/// <see cref="EnqueueResultType.QueuedForAnotherSinger"/>. Its <see cref="Models.Performance.SingerId"/>
/// and <see cref="Models.Performance.SungAs"/> name who holds the song.</param>
public sealed record EnqueueResult(
    EnqueueResultType Type,
    Performance? Performance = null,
    string? Reason = null,
    Performance? Conflict = null);
