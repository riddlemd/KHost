namespace KHost.Abstractions.Models.Plugins;

/// <summary>The plugin API version this host understands.</summary>
public static class PluginApi
{
    /// <summary>Bumped only on breaking changes to the plugin-facing contracts. A manifest whose
    /// <see cref="PluginManifest.ApiVersion"/> does not match this exactly is refused, and its
    /// catalog release is never offered to this host.</summary>
    public const int CurrentVersion = 4;
}
