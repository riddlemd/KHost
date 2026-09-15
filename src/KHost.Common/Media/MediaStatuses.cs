using KHost.Abstractions.Models;

namespace KHost.Common.Media;

public static class MediaStatuses
{
    /// <summary>
    /// The same question <see cref="IsAcquiring"/> asks, as data — an extension method has no SQL,
    /// so a LINQ-to-entities query has to use this instead and gets a translated IN clause.
    /// </summary>
    public static readonly MediaStatus[] Acquiring = [MediaStatus.Downloading, MediaStatus.Processing];

    /// <summary>
    /// True while the host is still bringing the file in. Downloading and Processing are two
    /// phases of one acquisition, not two kinds of state: neither is settled, and exactly one of
    /// Ready or Broken still follows. Anything asking "is this row in flight" wants both — asking
    /// for Downloading alone strands a row that has reached phase two.
    /// </summary>
    public static bool IsAcquiring(this MediaStatus status)
        => status is MediaStatus.Downloading or MediaStatus.Processing;
}
