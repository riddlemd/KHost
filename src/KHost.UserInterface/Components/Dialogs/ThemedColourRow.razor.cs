using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>One colour a venue may set, or leave to what it falls back to: unset, the box shows the
/// fallback and stores null; picking a colour stores it, and its "×" hands it back.</summary>
public partial class ThemedColourRow
{
    [Parameter, EditorRequired] public string Id { get; set; } = "";
    [Parameter, EditorRequired] public string Label { get; set; } = "";

    /// <summary>The venue's own colour as <c>#rrggbb</c>; null to take the fallback.</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>What is drawn with nothing set, as <c>#rrggbb</c>; the picker starts on it.</summary>
    [Parameter, EditorRequired] public string Fallback { get; set; } = "#000000";

    /// <summary>The line under the label with nothing set, saying where the colour comes from.</summary>
    [Parameter, EditorRequired] public string FallbackNote { get; set; } = "";

    /// <summary>The line under the label once set.</summary>
    [Parameter] public string OwnNote { get; set; } = "This venue's own colour.";

    [Parameter] public string ClearTitle { get; set; } = "Clear this venue's colour";

    private string Note => Value is null ? FallbackNote : OwnNote;
}
