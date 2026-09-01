using KHost.Abstractions.Models.QueueRotation;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class SingerQueueDialog
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter, EditorRequired] public QueueRotationConfig Config { get; set; } = new();
    [Parameter] public EventCallback OnClose { get; set; }
}
