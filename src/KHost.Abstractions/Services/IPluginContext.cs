namespace KHost.Abstractions.Services;

/// <summary>
/// What the host hands a plugin: its settings, and a way to tell the host something the person
/// running it should know. Everything else a plugin needs it takes in its constructor — the host's
/// container builds it, so any registered service is available the same way it is to the host. Setting values come from what the host collected for the manifest's
/// settings schema and are fixed for the lifetime of the process (restart applies changes).
/// </summary>
public interface IPluginContext
{

    T? GetSetting<T>(string key);

    /// <summary>
    /// Deserializes every stored setting into <typeparamref name="TSettings"/>. Manifest
    /// defaults fill missing keys; the class's property initializers cover the rest.
    /// </summary>
    TSettings BindSettings<TSettings>() where TSettings : new();

    /// <summary>
    /// Shows a line against this plugin on the Plugins page. For setup a host can act on — a
    /// slower path taken, a tool that would work better installed — not for failures, which
    /// belong in the exception that caused them.
    /// </summary>
    void ReportWarning(string message);
}
