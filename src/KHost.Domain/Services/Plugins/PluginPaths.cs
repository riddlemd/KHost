namespace KHost.Domain.Services.Plugins;

/// <summary>Where plugin folders live.</summary>
/// <remarks>Staging sits beside <c>plugins/</c>: Discover treats every subfolder as a plugin.</remarks>
public static class PluginPaths
{
    /// <summary>Marker file parked beside the staging folder to delete a plugin on the next start.</summary>
    public const string RemovalSuffix = ".remove";

    /// <summary>A staged payload the last start could not apply; renamed so it stops retrying.</summary>
    public const string FailureSuffix = ".failed";

    public const string FailureFileName = "error.txt";

    /// <summary>Download/extraction scratch, kept in staging so the final move stays on one volume.</summary>
    public const string WorkFolderName = ".work";

    public static string Plugins => Path.Combine(AppContext.BaseDirectory, "plugins");

    public static string Staging => Path.Combine(AppContext.BaseDirectory, "plugins-staging");

    public static string Cache => Path.Combine(AppContext.BaseDirectory, "cache");
}
