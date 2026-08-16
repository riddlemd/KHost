using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class Scrim
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
}
