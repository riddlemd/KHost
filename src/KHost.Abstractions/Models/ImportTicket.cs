namespace KHost.Abstractions.Models;

/// <summary>From BeginImportAsync; on cancellation, settle it via Complete/Fail/Discard.</summary>
public record ImportTicket
{
    public required Guid MediaId { get; init; }
    public CancellationToken Cancellation { get; init; }
}
