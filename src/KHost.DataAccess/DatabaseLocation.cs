using KHost.Common.Storage;

namespace KHost.DataAccess;

/// <summary>Where the library lives; a second copy of this path plants a db nobody else finds.</summary>
/// <remarks>Under <see cref="AppPaths.DataRoot"/>, so a throwaway console app reaches the runtime db by
/// setting <c>KHOST_DATA_ROOT</c> to the host's. Seed through the repositories, not the sqlite3 CLI:
/// the system binary lacks fts5 and dies on the media_fts triggers, and the folded columns are
/// written by EntityFolding on save.</remarks>
internal static class DatabaseLocation
{
    internal static string FilePath => Path.Combine(AppPaths.Cache, "khost.db");

    internal static string DirectoryPath => Path.GetDirectoryName(FilePath)!;
}
