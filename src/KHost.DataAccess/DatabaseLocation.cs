namespace KHost.DataAccess;

/// <summary>Where the library lives; a second copy of this path plants a db nobody else finds.</summary>
/// <remarks>Off AppContext.BaseDirectory, so a throwaway console app reaches the runtime db by
/// symlinking <c>cache</c> into its own output. Seed through the repositories, not the sqlite3 CLI:
/// the system binary lacks fts5 and dies on the media_fts triggers, and the folded columns are
/// written by EntityFolding on save.</remarks>
internal static class DatabaseLocation
{
    internal static string FilePath => Path.Combine(AppContext.BaseDirectory, "cache", "khost.db");

    internal static string DirectoryPath => Path.GetDirectoryName(FilePath)!;
}
