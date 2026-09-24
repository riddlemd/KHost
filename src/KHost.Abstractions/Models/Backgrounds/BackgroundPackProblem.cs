namespace KHost.Abstractions.Models.Backgrounds;

/// <summary>Why a pack yielded nothing to choose from.</summary>
/// <remarks>Named rather than a message, so the dialog can word each one for a host: "none chosen
/// yet" and "that folder is gone" want different advice.</remarks>
public enum BackgroundPackProblem
{
    /// <summary>No problem; the pack read normally.</summary>
    None,

    /// <summary>No pack folder has been chosen yet.</summary>
    NoFolderSet,

    /// <summary>The configured folder no longer exists or cannot be read.</summary>
    FolderMissing,
}
