namespace KHost.Abstractions.Services;

/// <summary>How a button appears now; default is enabled with the manifest label.</summary>
public sealed record PluginButtonState
{
    /// <summary>False leaves the button off the row.</summary>
    public bool Visible { get; init; } = true;
    /// <summary>False shows the button but refuses a press.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Overrides the manifest label when set; null keeps it.</summary>
    public string? Label { get; init; }

    /// <summary>The unchanged appearance: shown, enabled, manifest label.</summary>
    public static readonly PluginButtonState Default = new();
}

/// <summary>Puts buttons on the Plugins-page row; often shares state with the plugin's other roles.</summary>
/// <remarks>An extension point: a plugin IMPLEMENTS it alongside the buttons its manifest declares,
/// and the host reaches it by plugin id. The plugin's object is one singleton shared across every
/// extension interface it implements, so a sign-in button and the search it unlocks see the same
/// session. Called from any thread.</remarks>
public interface IPluginButtonHandler
{
    /// <summary>Runs the button's action. An unknown key is a no-op.</summary>
    /// <param name="key">The button's key from the manifest.</param>
    /// <param name="cancellationToken">Honour it if it fires; the host does not cancel a press.</param>
    /// <remarks>The host re-reads <see cref="DescribeButton"/> once it returns, so one button can
    /// change its own label, hide or disable itself. A second press on the same button is ignored
    /// until this returns. A throw is logged and the page stays up, so report failures a host should
    /// see some other way.</remarks>
    Task InvokeButtonAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>How the button looks now; the default suits a button that never changes.</summary>
    /// <remarks>Asked whenever the row is drawn, so answer from memory. A throw is treated as
    /// <see cref="PluginButtonState.Default"/>. Has a default body returning
    /// <see cref="PluginButtonState.Default"/>; override it for a button that toggles, hides or
    /// disables itself.</remarks>
    PluginButtonState DescribeButton(string key) => PluginButtonState.Default;
}
