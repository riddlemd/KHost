using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Fetches again the file of a library row this provider made, after the host removed it.</summary>
/// <remarks>An extension point a plugin implements alongside the imports that mark rows
/// <see cref="Media.IsEphemeral"/> or <see cref="Media.IsSingleUse"/>. The host asks when a
/// <see cref="MediaStatus.NotDownloaded"/> row is queued, or a host presses Download now.
///
/// <para>The host has already moved the row to <see cref="MediaStatus.Downloading"/> and opened its
/// Downloads entry before <see cref="RefetchAsync"/> runs, so the provider does only what it does
/// after <see cref="IMediaAcquisitionService.BeginImportAsync"/>: write the file to
/// <see cref="Media.FilePath"/>, report progress, and settle the ticket's row exactly once through
/// <see cref="IMediaAcquisitionService"/>.</para></remarks>
[PluginExtensionPoint]
public interface IMediaRefetcher
{
    /// <summary>Whether this provider can fetch the row's file again.</summary>
    /// <remarks>Answers from the row alone (its <see cref="Media.Source"/> and
    /// <see cref="Media.SourceKey"/>), without touching the disk or the network: it is asked for
    /// every refetch, and for a dialog that only wants to know whether to offer one.</remarks>
    bool CanRefetch(Media media);

    /// <summary>Downloads the row's file again and settles the import.</summary>
    /// <remarks>Runs in the background; the host does not wait on it. Watch
    /// <see cref="ImportTicket.Cancellation"/>, and settle with
    /// <see cref="IMediaAcquisitionService.DiscardImportAsync"/> when it fires. A throw is logged and
    /// settles the row as failed with no reason.</remarks>
    Task RefetchAsync(Media media, ImportTicket ticket);
}
