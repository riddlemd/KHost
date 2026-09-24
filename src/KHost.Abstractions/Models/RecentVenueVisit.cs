namespace KHost.Abstractions.Models;

/// <summary>One venue a singer sang at, and the last time they did.</summary>
/// <param name="VenueId">The venue's id.</param>
/// <param name="LastSungOn">When the singer last sang there, in UTC.</param>
public sealed record RecentVenueVisit(Guid VenueId, DateTime LastSungOn);
