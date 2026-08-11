namespace KHost.Abstractions.Models;

/// <summary>A venue a singer has sung at, with the date they most recently sang there.</summary>
public sealed record RecentVenueVisit(Guid VenueId, DateTime LastSungOn);
