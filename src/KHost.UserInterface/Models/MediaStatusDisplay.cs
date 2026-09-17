using KHost.Abstractions.Models;

namespace KHost.UserInterface.Models;

public static class MediaStatusDisplay
{
    /// <summary>Shared so the manager table and the edit dialog can't drift apart.</summary>
    public static string BadgeClass(MediaStatus status) => status switch
    {
        MediaStatus.Ready       => "kh-badge--success",
        // Processing shares Downloading's colour on purpose: it's the second phase of that
        // download, and the badge word says which; a colour of its own would misread as a state.
        MediaStatus.Downloading => "kh-badge--info",
        MediaStatus.Processing  => "kh-badge--info",
        MediaStatus.Broken      => "kh-badge--danger",
        _                       => "kh-badge--secondary",
    };

    /// <summary>Only Ready and Broken are the host's to set.</summary>
    /// <remarks>Downloading/Processing belong to the worker; Unknown means none was ever set.</remarks>
    public static bool IsUserSettable(MediaStatus status)
        => status is MediaStatus.Ready or MediaStatus.Broken;
}
