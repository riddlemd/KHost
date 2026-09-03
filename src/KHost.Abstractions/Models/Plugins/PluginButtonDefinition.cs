namespace KHost.Abstractions.Models.Plugins;

/// <summary>
/// A button on a plugin's row on the Plugins page, declared in the manifest. The plugin implements
/// <see cref="Services.IPluginButtonHandler"/> to run it when it is clicked, and may say through the
/// same interface how it should look right now — the login button that reads "Sign in" or "Sign out"
/// depending on whether a session is open.
/// </summary>
public class PluginButtonDefinition
{
    public required string Key { get; set; }

    /// <summary>The wording when the handler does not override it — see <see cref="Services.PluginButtonState.Label"/>.</summary>
    public required string Label { get; set; }

    /// <summary>
    /// A <c>kh-button</c> modifier: "primary" (the default), "secondary" or "danger". Presentation
    /// only, and left to the host to sanitise — an unknown value falls back to the default.
    /// </summary>
    public string? Style { get; set; }
}
