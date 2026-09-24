namespace KHost.Abstractions.Models.Plugins;

/// <summary>A button in a plugin's table: on one row, or above the table for the whole thing.</summary>
/// <remarks><see cref="PerformAsync"/> is a delegate rather than a key the host dispatches back,
/// the same shape as <see cref="MediaProviderAction"/>: the plugin closes over whatever the action
/// needs, so the host carries no map from strings to behaviour.</remarks>
public sealed record PluginTableAction
{
    /// <summary>The button's label.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Bootstrap Icons name without the <c>bi-</c> prefix; unknown draws nothing.</summary>
    public string? Icon { get; init; }

    /// <summary>A <c>kh-button</c> modifier; an unknown value falls back to the default.</summary>
    public string? Style { get; init; }

    /// <summary>Hover text. Null shows the display name alone.</summary>
    public string? Description { get; init; }

    /// <summary>Drawn pressed, for an action that is currently on — a running search.</summary>
    public bool IsActive { get; init; }

    /// <summary>Runs when the button is pressed. The dialog re-reads the table once it completes.</summary>
    public required Func<CancellationToken, Task> PerformAsync { get; init; }
}
