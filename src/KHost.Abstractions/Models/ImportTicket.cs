namespace KHost.Abstractions.Models;

/// <summary>
/// Handed back from <see cref="Services.IPluginLibrary.BeginImportAsync"/>. The token fires when
/// the host cancels this download — shutdown included. On cancellation the plugin must stop,
/// clean up any partial file, then call exactly one of <see cref="Services.IPluginLibrary.CompleteImportAsync"/>,
/// <see cref="Services.IPluginLibrary.FailImportAsync"/>, or <see cref="Services.IPluginLibrary.DiscardImportAsync"/>.
/// </summary>
public record ImportTicket
{
    public required Guid MediaId { get; init; }
    public CancellationToken Cancellation { get; init; }
}
