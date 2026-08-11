namespace KHost.Abstractions.Models.Plugins;

public enum PluginStatus
{
    /// <summary>Discovered but not in the enabled list; nothing was loaded.</summary>
    Disabled,
    /// <summary>Enabled and valid; assemblies not loaded yet.</summary>
    Enabled,
    /// <summary>Loaded into the process and its extensions registered.</summary>
    Loaded,
    /// <summary>Built against an unsupported plugin API version; never loaded.</summary>
    Incompatible,
    /// <summary>Bad manifest, missing assembly, or a load/scan failure — see Error.</summary>
    Errored,
}
