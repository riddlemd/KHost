using KHost.Abstractions.Models;

namespace KHost.UserInterface.Models;

public static class MediaStatusDisplay
{
    /// <summary>Shared so the manager table and the edit dialog can't drift apart.</summary>
    public static string BadgeClass(MediaStatus status) => status switch
    {
        MediaStatus.Ready       => "kh-badge--success",
        MediaStatus.Downloading => "kh-badge--info",
        MediaStatus.Broken      => "kh-badge--danger",
        _                       => "kh-badge--secondary",
    };

    /// <summary>
    /// Downloading and Processing belong to whatever is doing the work, and Unknown means we
    /// never established one — only the two settled states are the host's to set by hand.
    /// </summary>
    public static bool IsUserSettable(MediaStatus status)
        => status is MediaStatus.Ready or MediaStatus.Broken;
}
