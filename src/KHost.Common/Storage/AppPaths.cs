namespace KHost.Common.Storage;

/// <summary>Where KHost keeps what it writes: the library database, settings, logs, plugins and screen keys.</summary>
/// <remarks>Beside the executable on Windows and Linux, so separate installs keep separate data. On macOS the
/// executable sits inside a signed <c>KHost.app</c>, which must not be written to, so the data lives in
/// <c>~/Library/Application Support/KHost</c>. <c>KHOST_DATA_ROOT</c> overrides both, e.g. to keep a test run's data
/// apart.</remarks>
public static class AppPaths
{
    /// <summary>The environment variable that overrides <see cref="DataRoot"/>.</summary>
    public const string OverrideVariable = "KHOST_DATA_ROOT";

    /// <summary>The folder KHost's data lives in; created on first use.</summary>
    public static string DataRoot { get; } = Create(Resolve(
        Environment.GetEnvironmentVariable(OverrideVariable),
        OperatingSystem.IsMacOS(),
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

    /// <summary>Settings, the database, screen keys and other state that outlives a run.</summary>
    public static string Cache => Path.Combine(DataRoot, "cache");

    /// <summary>The host's and the screens' log files.</summary>
    public static string Logs => Path.Combine(DataRoot, "logs");

    /// <summary>Picks the data folder for an OS, an executable folder and a home folder.</summary>
    /// <param name="overrideRoot">The <see cref="OverrideVariable"/> value, if set.</param>
    /// <param name="isMacOS">Whether this is macOS.</param>
    /// <param name="baseDirectory">The executable's folder.</param>
    /// <param name="home">The user's home folder.</param>
    /// <returns>The folder, not yet created.</returns>
    public static string Resolve(string? overrideRoot, bool isMacOS, string baseDirectory, string home)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot)) return Path.GetFullPath(overrideRoot);
        return isMacOS ? Path.Combine(home, "Library", "Application Support", "KHost") : baseDirectory;
    }

    /// <summary>Copies a build's data from beside its executable into a new <paramref name="dataRoot"/>, once.</summary>
    /// <remarks>Only while <paramref name="dataRoot"/> holds no data yet: a later run must never overwrite the
    /// library with an old copy.</remarks>
    /// <param name="legacyRoot">Where the data used to live: the executable's folder.</param>
    /// <param name="dataRoot">Where it lives now.</param>
    /// <returns>Whether anything was copied.</returns>
    public static bool AdoptLegacyData(string legacyRoot, string dataRoot)
    {
        if (Path.GetFullPath(legacyRoot) == Path.GetFullPath(dataRoot)) return false;
        if (Directory.Exists(Path.Combine(dataRoot, "cache"))) return false;

        var copied = false;
        foreach (var folder in new[] { "cache", "plugins" })
        {
            var from = Path.Combine(legacyRoot, folder);
            if (!Directory.Exists(from)) continue;
            CopyDirectory(from, Path.Combine(dataRoot, folder));
            copied = true;
        }
        return copied;
    }

    private static string Create(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);
        foreach (var directory in Directory.EnumerateDirectories(from))
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
    }
}
