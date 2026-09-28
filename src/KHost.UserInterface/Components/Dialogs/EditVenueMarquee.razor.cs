using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>The venue dialog's "Screen marquee" section. Binds directly to the shared
/// <see cref="EditVenueModel"/>, so it needs nothing of its own beyond the ambient EditContext the
/// dialog's EditForm already cascades.</summary>
public partial class EditVenueMarquee
{
    [Parameter, EditorRequired] public EditVenueModel Model { get; set; } = default!;
}
