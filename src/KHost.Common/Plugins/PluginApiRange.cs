using KHost.Abstractions.Models.Plugins;

namespace KHost.Common.Plugins;

/// <summary>A span of plugin API versions a host runs, both ends inclusive.</summary>
/// <param name="Minimum">The oldest plugin API the host still runs.</param>
/// <param name="Current">The newest plugin API the host offers.</param>
public readonly record struct PluginApiRange(int Minimum, int Current)
{
    /// <summary>The range this host runs: <see cref="PluginApi.MinimumVersion"/> to
    /// <see cref="PluginApi.CurrentVersion"/>.</summary>
    public static PluginApiRange ThisHost { get; } = new(PluginApi.MinimumVersion, PluginApi.CurrentVersion);

    /// <summary>True when a plugin built against <paramref name="apiVersion"/> runs on a host with
    /// this range.</summary>
    public bool Covers(int apiVersion) => Minimum <= apiVersion && apiVersion <= Current;

    /// <summary>Why a plugin built against <paramref name="apiVersion"/> does not run here, in words a
    /// host can act on: whether KHost or the plugin needs the update. Null when it runs.</summary>
    public string? DescribeRefusal(int apiVersion)
    {
        if (apiVersion > Current)
            return $"Needs a newer KHost (built for plugin API {apiVersion}; this KHost runs {Describe()}).";

        if (apiVersion < Minimum)
            return $"Built for an older KHost (plugin API {apiVersion}; this KHost runs {Describe()}); it needs an update.";

        return null;
    }

    private string Describe() => Minimum == Current ? $"{Current}" : $"{Minimum}–{Current}";
}
