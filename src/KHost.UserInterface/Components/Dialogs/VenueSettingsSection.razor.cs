using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>One titled section of the venue dialog, collapsed until the host opens it.</summary>
public partial class VenueSettingsSection
{
    [Parameter, EditorRequired] public string Title { get; set; } = "";

    [Parameter] public RenderFragment? ChildContent { get; set; }
}
