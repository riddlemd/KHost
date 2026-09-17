namespace KHost.Abstractions.Services;

/// <summary>How a button appears now; default is enabled with the manifest label.</summary>
public sealed record PluginButtonState
{
    public bool Visible { get; init; } = true;
    public bool Enabled { get; init; } = true;

    /// <summary>Overrides the manifest label when set; null keeps it.</summary>
    public string? Label { get; init; }

    /// <summary>The unchanged appearance: shown, enabled, manifest label.</summary>
    public static readonly PluginButtonState Default = new();
}

/// <summary>Puts buttons on the Plugins-page row; often shares state with the plugin's other roles.</summary>
public interface IPluginButtonHandler
{
    /// <summary>Runs the button's action. An unknown key is a no-op.</summary>
    Task InvokeButtonAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>How the button looks now; the default suits a button that never changes.</summary>
    PluginButtonState DescribeButton(string key) => PluginButtonState.Default;
}
