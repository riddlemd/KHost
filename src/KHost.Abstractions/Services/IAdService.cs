namespace KHost.Abstractions.Services;

/// <summary>
/// Decides when an ad plays; the pool decides which. A venue runs one ad playlist at a time, so
/// there is no priority and nothing to resolve between two schedules coming due at once.
/// </summary>
public interface IAdService
{

    /// <summary>False when the venue has chosen no ad playlist, which is most venues.</summary>
    bool IsConfigured { get; }

    /// <summary>Performances since the last ad played, for the every-N-songs trigger.</summary>
    int PerformancesSinceLastAd { get; }

    DateTimeOffset? LastAdAtUtc { get; }

    /// <summary>Starts the every-N-minutes clock, so the first ad waits rather than firing at once.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The host's button: plays one now whatever the playlist's trigger says. Still refused over
    /// a loaded performance, because nothing gets to cut a singer short.
    /// </summary>
    Task<bool> PlayNowAsync(CancellationToken cancellationToken = default);
}
