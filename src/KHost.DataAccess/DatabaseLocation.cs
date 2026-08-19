namespace KHost.DataAccess;

/// <summary>
/// Where the library lives. Registration builds the connection string from it and the initializer
/// creates the folder, so a second copy of the expression is a database quietly created somewhere
/// the other half never looks.
/// </summary>
internal static class DatabaseLocation
{
    internal static string FilePath => Path.Combine(AppContext.BaseDirectory, "cache", "khost.db");

    internal static string DirectoryPath => Path.GetDirectoryName(FilePath)!;
}
