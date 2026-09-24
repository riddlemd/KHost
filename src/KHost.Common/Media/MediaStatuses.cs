using KHost.Abstractions.Models;

namespace KHost.Common.Media;

/// <summary>Groupings over <see cref="MediaStatus"/> that more than one caller needs to agree on.</summary>
public static class MediaStatuses
{
    /// <summary>The same question <see cref="IsAcquiring"/> asks, as data, for LINQ-to-entities.</summary>
    /// <remarks>An extension method has no SQL; this translates to an IN clause instead.</remarks>
    public static readonly MediaStatus[] Acquiring = [MediaStatus.Downloading, MediaStatus.Processing];

    /// <summary>True while still being brought in: one acquisition, two phases.</summary>
    /// <remarks>Asking for Downloading alone strands a row that reached phase two.</remarks>
    public static bool IsAcquiring(this MediaStatus status)
        => status is MediaStatus.Downloading or MediaStatus.Processing;
}
