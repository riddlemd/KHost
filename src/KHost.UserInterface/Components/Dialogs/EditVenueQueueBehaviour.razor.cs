using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>The venue dialog's "Queue behavior" section, including the rotation strategy dialog it
/// opens: that dialog edits <see cref="EditVenueModel.QueueRotation"/> in place, so it belongs with
/// the section that offers it rather than back up at the top-level dialog.</summary>
public partial class EditVenueQueueBehaviour
{
    [Parameter, EditorRequired] public EditVenueModel Model { get; set; } = default!;

    private bool _rotationDialogOpen;

    private void CloseRotationDialog() => _rotationDialogOpen = false;
}
