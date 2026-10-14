namespace KHost.Abstractions.Models.Plugins;

/// <summary>Where a discovered plugin stands, for the Plugins page.</summary>
public enum PluginStatus
{
    /// <summary>Discovered but not in the enabled list; nothing was loaded.</summary>
    Disabled = 0,
    /// <summary>Enabled and valid; assemblies not loaded yet.</summary>
    Enabled = 1,
    /// <summary>Loaded into the process and its extensions registered.</summary>
    Loaded = 2,
    /// <summary>Needs contracts this host does not have; never loaded.</summary>
    Incompatible = 3,
    /// <summary>Bad manifest, missing assembly, or a load/scan failure; see Error.</summary>
    Errored = 4,
}
