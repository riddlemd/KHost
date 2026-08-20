using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class RedirectToLogin
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized() => Navigation.NavigateTo("/login");
}
