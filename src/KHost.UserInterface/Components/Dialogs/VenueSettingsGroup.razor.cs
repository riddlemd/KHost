using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>A run of related rows inside a venue settings section, under its own heading.</summary>
public partial class VenueSettingsGroup
{
    [Parameter, EditorRequired] public string Title { get; set; } = "";

    /// <summary>A short line at the heading's far side, as when the rows apply.</summary>
    [Parameter] public string? Note { get; set; }

    [Parameter, EditorRequired] public RenderFragment ChildContent { get; set; } = default!;
}
