namespace KHost.Abstractions.Models;

/// <summary>From BeginImportAsync; on cancellation, settle it via Complete/Fail/Discard.</summary>
public record ImportTicket
{
    /// <summary>The library row created or reused for this import.</summary>
    public required Guid MediaId { get; init; }

    /// <summary>Cancelled if this same import is cancelled elsewhere before it settles.</summary>
    public CancellationToken Cancellation { get; init; }
}
