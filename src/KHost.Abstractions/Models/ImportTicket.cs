namespace KHost.Abstractions.Models;

/// <summary>
/// Handed back from <see cref="Services.IMediaAcquisitionService.BeginImportAsync"/>. The token fires when
/// the host cancels this download — shutdown included. On cancellation the plugin must stop,
/// clean up any partial file, then call exactly one of <see cref="Services.IMediaAcquisitionService.CompleteImportAsync"/>,
/// <see cref="Services.IMediaAcquisitionService.FailImportAsync"/>, or <see cref="Services.IMediaAcquisitionService.DiscardImportAsync"/>.
/// </summary>
public record ImportTicket
{
    public required Guid MediaId { get; init; }
    public CancellationToken Cancellation { get; init; }
}
