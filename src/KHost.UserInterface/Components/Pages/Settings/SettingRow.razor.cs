using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

/// <summary>One row of AppSettingsPage: a label, when it applies, an optional note, and the control.</summary>
public partial class SettingRow
{
    [Parameter, EditorRequired] public string Label { get; set; } = "";
    [Parameter, EditorRequired] public string Applies { get; set; } = "";
    [Parameter] public bool IsCheckbox { get; set; }
    [Parameter] public RenderFragment? Note { get; set; }
    [Parameter, EditorRequired] public RenderFragment ChildContent { get; set; } = default!;

    // A checkbox row always carries the check label styling; a plain row switches to the
    // labelled wrapper only once a note needs the extra column, matching the markup this replaces.
    private string RowClass => IsCheckbox ? "kh-app-settings__row kh-form-check" : "kh-app-settings__row";
    private string? LabelClass => IsCheckbox ? "kh-form-check-label" : (Note is null ? null : "kh-app-settings__labelled");
}
