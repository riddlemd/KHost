namespace KHost.Abstractions.Services;

/// <summary>Decides when an ad plays; the pool decides which. One playlist per venue, no competing.</summary>
public interface IAdService
{

    /// <summary>False when the venue has chosen no ad playlist, which is most venues.</summary>
    bool IsConfigured { get; }

    /// <summary>Performances since the last ad played, for the every-N-songs trigger.</summary>
    int PerformancesSinceLastAd { get; }

    DateTimeOffset? LastAdAtUtc { get; }

    /// <summary>Starts the every-N-minutes clock, so the first ad waits rather than firing at once.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>The host's button: plays now regardless of trigger; refused over a performance.</summary>
    Task<bool> PlayNowAsync(CancellationToken cancellationToken = default);
}
