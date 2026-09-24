namespace KHost.Abstractions.Services;

/// <summary>Decides when an ad plays; the pool decides which. One playlist per venue, no competing.</summary>
/// <remarks>Host-owned: a plugin has no reason to implement it, and may take it in its constructor
/// to read whether ads are set up or to play one on demand. A host singleton, callable from any
/// thread; concurrent calls do not interleave. Announces
/// <see cref="KHost.Abstractions.Messaging.Messages.AdsChanged"/> whenever the counters or
/// <see cref="IsConfigured"/> move. The automatic trigger runs in the gap after each performance
/// (see <see cref="IPlaybackService.PerformanceEnded"/>), and a failure there is logged rather than
/// allowed to stop the queue.</remarks>
public interface IAdService
{

    /// <summary>False when the venue has chosen no ad playlist, which is most venues.</summary>
    /// <remarks>True only when the chosen playlist actually resolves, not merely when an id is set.
    /// Re-read when the selected venue changes.</remarks>
    bool IsConfigured { get; }

    /// <summary>Performances since the last ad played, for the every-N-songs trigger.</summary>
    /// <remarks>Reset only by an ad that reached the screen; a refused ad leaves the slot due.</remarks>
    int PerformancesSinceLastAd { get; }

    /// <summary>When the last ad started, for the every-N-minutes trigger. Null until
    /// <see cref="InitializeAsync"/> stamps the start of the night.</summary>
    DateTimeOffset? LastAdAtUtc { get; }

    /// <summary>Starts the every-N-minutes clock, so the first ad waits rather than firing at once.</summary>
    /// <remarks>Called once by the host at startup; a plugin should not call it.</remarks>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>The host's button: plays now regardless of trigger; refused over a performance.</summary>
    /// <returns>False when the venue has no ad playlist, the playlist holds nothing playable, the
    /// entry does not resolve, or playback refused it (a performance is loaded, no display is
    /// connected, or the media is not Ready).</returns>
    Task<bool> PlayNowAsync(CancellationToken cancellationToken = default);
}
