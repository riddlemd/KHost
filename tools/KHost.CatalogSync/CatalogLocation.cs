namespace KHost.CatalogSync;

/// <summary>Where the published catalog lives when <c>--catalog</c> is not given.</summary>
public static class CatalogLocation
{
    /// <summary>The catalog file name in the KHost.Releases repo.</summary>
    public const string FileName = "plugins.json";

    /// <summary>plugins.json in the KHost.Releases checkout beside the KHost repo holding
    /// <paramref name="startDirectory"/>, or null when there is none.</summary>
    /// <remarks>Tries the per-branch layout ({root}/{owner}/{repo}/{branch}/) first, then repos
    /// checked out flat as siblings.</remarks>
    public static string? FindDefault(string startDirectory)
    {
        var repoRoot = FindRepoRoot(startDirectory) ?? startDirectory;

        string[] candidates =
        [
            Path.Combine(repoRoot, "..", "..", "KHost.Releases", "main", FileName),
            Path.Combine(repoRoot, "..", "KHost.Releases", FileName),
        ];

        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    // dotnet run keeps the caller's directory, which may be anywhere inside the repo.
    private static string? FindRepoRoot(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KHost.slnx")))
                return directory.FullName;
        }

        return null;
    }
}
