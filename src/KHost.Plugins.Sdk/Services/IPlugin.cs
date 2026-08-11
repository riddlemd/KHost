namespace KHost.Plugins.Sdk.Services;

/// <summary>
/// Host-provided context for a loaded plugin. Setting values come from what the host
/// collected for the manifest's settings schema and are fixed for the lifetime of the
/// process (restart applies changes).
/// </summary>
public interface IPlugin
{
    T? GetSetting<T>(string key);

    /// <summary>
    /// Deserializes every stored setting into <typeparamref name="TSettings"/>. Manifest
    /// defaults fill missing keys; the class's property initializers cover the rest.
    /// </summary>
    TSettings BindSettings<TSettings>() where TSettings : new();
}
