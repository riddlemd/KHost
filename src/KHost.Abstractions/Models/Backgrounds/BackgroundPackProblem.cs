namespace KHost.Abstractions.Models.Backgrounds;

/// <summary>Why a pack yielded nothing to choose from.</summary>
/// <remarks>Named rather than a message, so the dialog can word each one for a host: "none chosen
/// yet" and "that folder is gone" want different advice.</remarks>
public enum BackgroundPackProblem
{
    None,
    NoFolderSet,
    FolderMissing,
}
