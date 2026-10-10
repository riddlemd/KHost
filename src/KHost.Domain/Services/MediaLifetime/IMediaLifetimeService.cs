using KHost.Abstractions.Models;

namespace KHost.Domain.Services.MediaLifetime;

/// <summary>Removes and restores the files of library rows a provider marked ephemeral or
/// single-use, keeping the rows themselves.</summary>
/// <remarks>Domain, not Abstractions: it deletes files, and the NotDownloaded transition is the
/// host's alone, so no plugin may reach it.</remarks>
public interface IMediaLifetimeService
{
    /// <summary>Starts fetching a <see cref="MediaStatus.NotDownloaded"/> row's file again, without waiting for it.</summary>
    /// <returns>False when no installed provider can, or the row is not waiting for its file.</returns>
    Task<bool> RefetchAsync(Media media);

    /// <summary>Deletes every ephemeral or single-use row's file, for the host closing, except one a
    /// queued turn still waits on.</summary>
    Task RemoveFilesOnCloseAsync(IReadOnlyCollection<Guid> queuedMediaIds);

    /// <summary>Deletes a single-use row's file once one of its performances has been sung, when no
    /// queued turn still waits on it.</summary>
    Task RemoveSingleUseFileIfDoneAsync(Guid mediaId, IReadOnlyCollection<Guid> queuedMediaIds);
}
