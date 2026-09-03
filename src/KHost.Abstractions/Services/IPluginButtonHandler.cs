namespace KHost.Abstractions.Services;

/// <summary>
/// How a button on a plugin's Plugins-page row should appear right now. The default shows it,
/// enabled, with the label from the manifest; a handler returns a changed one to relabel a toggle
/// (a session button that reads "Sign out" once a session is open), grey it out, or hide it.
/// </summary>
public sealed record PluginButtonState
{
    public bool Visible { get; init; } = true;
    public bool Enabled { get; init; } = true;

    /// <summary>Overrides the manifest label when set; null keeps it.</summary>
    public string? Label { get; init; }

    /// <summary>The unchanged appearance: shown, enabled, manifest label.</summary>
    public static readonly PluginButtonState Default = new();
}

/// <summary>
/// A plugin that puts buttons on its Plugins-page row implements this. The buttons themselves are
/// declared in the manifest (<see cref="Models.Plugins.PluginButtonDefinition"/>); this runs one
/// when the host clicks it, and optionally describes how it should look now. It is an ordinary
/// extension interface — the host scans the entry assembly for it — so the type that implements it
/// can be the same one that also implements a provider, and share its state: KaraFun's search
/// provider is its own session button.
/// </summary>
public interface IPluginButtonHandler
{
    /// <summary>Runs the button's action. An unknown key is a no-op.</summary>
    Task InvokeButtonAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>How the button should appear now. The default is fine for a button that never changes.</summary>
    PluginButtonState DescribeButton(string key) => PluginButtonState.Default;
}
