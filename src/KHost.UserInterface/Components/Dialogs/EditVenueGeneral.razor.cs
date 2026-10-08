using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>The venue dialog's "General" section: the placeholder image and what the "Up next"
/// card is drawn over.</summary>
public partial class EditVenueGeneral
{
    [Parameter, EditorRequired] public EditVenueModel Model { get; set; } = default!;
}
