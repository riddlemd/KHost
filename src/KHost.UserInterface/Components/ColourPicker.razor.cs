using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

/// <summary>A colour box. A clearable one may also hold no colour: it then shows
/// <see cref="Fallback"/>, and an "×" on a chosen colour clears it back to none.</summary>
/// <remarks>A native colour picker has no empty state, so "none" has to be drawn beside it.</remarks>
public partial class ColourPicker
{
    /// <summary>The input's id, for a label's <c>for</c>; the clear button is <c>{Id}-clear</c>.</summary>
    [Parameter] public string? Id { get; set; }

    /// <summary>The colour as <c>#rrggbb</c>; null for none.</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>What the box shows with no colour chosen.</summary>
    [Parameter] public string Fallback { get; set; } = "#000000";

    /// <summary>Whether a chosen colour can be cleared back to none.</summary>
    [Parameter] public bool Clearable { get; set; }

    [Parameter] public string ClearTitle { get; set; } = "Clear this colour";

    /// <summary>For a box with no label of its own.</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Classes added to the box, for a caller that places it.</summary>
    [Parameter] public string Class { get; set; } = "";

    private string? ClearId => Id is null ? null : $"{Id}-clear";

    private Task PickAsync(ChangeEventArgs e) => ValueChanged.InvokeAsync(e.Value?.ToString());

    private Task ClearAsync() => ValueChanged.InvokeAsync(null);
}
