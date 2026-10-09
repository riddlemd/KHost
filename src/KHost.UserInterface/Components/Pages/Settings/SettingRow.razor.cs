using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

/// <summary>One row of AppSettingsPage: a label, when it applies, and the control, with an optional
/// note under them.</summary>
public partial class SettingRow
{
    [Parameter, EditorRequired] public string Label { get; set; } = "";
    [Parameter, EditorRequired] public string Applies { get; set; } = "";
    [Parameter] public bool IsCheckbox { get; set; }
    [Parameter] public RenderFragment? Note { get; set; }
    [Parameter, EditorRequired] public RenderFragment ChildContent { get; set; } = default!;

    // The note goes under the row, across the panel's width, so the label column holds only the
    // name and when it applies and lines up with the control beside it.
    private string RowClass => IsCheckbox ? "kh-app-settings__row kh-form-check" : "kh-app-settings__row";
    private string LabelClass => IsCheckbox ? "kh-form-check-label" : "kh-app-settings__labelled";
}
