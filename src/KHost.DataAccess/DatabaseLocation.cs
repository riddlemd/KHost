namespace KHost.DataAccess;

/// <summary>Where the library lives; a second copy of this path plants a db nobody else finds.</summary>
internal static class DatabaseLocation
{
    internal static string FilePath => Path.Combine(AppContext.BaseDirectory, "cache", "khost.db");

    internal static string DirectoryPath => Path.GetDirectoryName(FilePath)!;
}
