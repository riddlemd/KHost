using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

/// <summary>The two "restart to apply" banners: settings/plugin state waiting on a restart, and a
/// staged install/removal waiting on one. The conditions are independent, so both may show at once.</summary>
public partial class PluginRestartBanner
{
    [Parameter, EditorRequired] public bool RestartRequired { get; set; }
    [Parameter, EditorRequired] public IReadOnlyList<string> Waiting { get; set; } = [];
    [Parameter, EditorRequired] public bool StagingIsEmpty { get; set; }
    [Parameter, EditorRequired] public string StagingSummary { get; set; } = "";
}
