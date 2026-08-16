using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KHost.UserInterface.Components;

public partial class Icon
{
    [Parameter]
    public string Name { get; set; } = "";

    [Parameter]
    public string Class { get; set; } = "";

    [Parameter]
    public EventCallback<MouseEventArgs> OnClick { get; set; }
}
