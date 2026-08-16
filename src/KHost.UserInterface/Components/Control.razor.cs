using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class Control
{
    [Parameter]
    public string Class { get; set; } = "";

    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
