using KHost.Abstractions.Models.Plugins;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

/// <summary>One plugin's settings fields, drawn from its PluginSettingsDraft. OnChanged fires on
/// every edit: the row's own dirty/saved buttons live outside this component's render output and
/// only redraw when told to, since mutating a field here does not by itself re-render the caller.</summary>
public partial class PluginSettingsForm
{
    [Parameter, EditorRequired] public PluginSettingsDraft? Draft { get; set; }
    [Parameter] public EventCallback OnChanged { get; set; }

    private void MarkEdited()
    {
        Draft?.MarkEdited();
        _ = OnChanged.InvokeAsync();
    }

    private void ReplaceSecret(SettingField field)
    {
        field.Replacing = true;
        field.Text = null;
        MarkEdited();
    }

    private void CancelReplaceSecret(SettingField field)
    {
        field.Replacing = false;
        field.Text = null;
        field.StoredSecret = field.OriginalSecret;
        MarkEdited();
    }

    private void ClearSecret(SettingField field)
    {
        field.Replacing = false;
        field.Text = null;
        field.StoredSecret = null;
        MarkEdited();
    }

    private static string GetInputType(PluginSettingDefinition definition)
        => definition.Type == PluginSettingType.Int ? "number" : "text";
}
