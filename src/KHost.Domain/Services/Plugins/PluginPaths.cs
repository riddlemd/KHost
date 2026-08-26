namespace KHost.Domain.Services.Plugins;

/// <summary>
/// Where plugin folders live. Staging is a sibling of <c>plugins/</c>, never a child:
/// <see cref="PluginLoader.Discover"/> treats every subdirectory of the plugins folder as a plugin,
/// so a staging folder nested inside it would render as a broken row on the Plugins page.
/// </summary>
public static class PluginPaths
{
    /// <summary>Marker file parked beside the staging folder to delete a plugin on the next start.</summary>
    public const string RemovalSuffix = ".remove";

    /// <summary>A staged payload the last start could not apply; renamed so it stops retrying.</summary>
    public const string FailureSuffix = ".failed";

    public const string FailureFileName = "error.txt";

    /// <summary>Download and extraction scratch, kept inside staging so the final move never
    /// crosses a volume — <c>Directory.Move</c> cannot, and the system temp folder often is one.</summary>
    public const string WorkFolderName = ".work";

    public static string Plugins => Path.Combine(AppContext.BaseDirectory, "plugins");

    public static string Staging => Path.Combine(AppContext.BaseDirectory, "plugins-staging");

    public static string Cache => Path.Combine(AppContext.BaseDirectory, "cache");
}
