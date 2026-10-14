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
    /// <summary>Built against a plugin API outside the range this host runs; never loaded.</summary>
    Incompatible = 3,
    /// <summary>Bad manifest, missing assembly, or a load/scan failure; see Error.</summary>
    Errored = 4,
}
