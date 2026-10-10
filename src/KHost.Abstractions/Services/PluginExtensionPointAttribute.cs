namespace KHost.Abstractions.Services;

/// <summary>Marks an interface a plugin can implement for the host to find and use.</summary>
/// <remarks>The host binds a plugin's implementation of every interface carrying this, and only
/// those: a plugin implementing any other contract (a host service, say) is not registered over the
/// host's own. A plugin type implementing several is one object behind all of them.</remarks>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class PluginExtensionPointAttribute : Attribute
{
    /// <summary>An extension point the Plugins page lists nothing for.</summary>
    public PluginExtensionPointAttribute() { }

    /// <summary>An extension point the Plugins page lists as a capability.</summary>
    /// <param name="capability">What the Plugins page calls a plugin providing it.</param>
    public PluginExtensionPointAttribute(string capability) => Capability = capability;

    /// <summary>What the Plugins page lists a plugin as providing, or null when it lists nothing for it.</summary>
    public string? Capability { get; }

    /// <summary>Where the capability sits among a plugin's others on the Plugins page; lower first.</summary>
    public int Order { get; init; }
}
