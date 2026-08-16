using KHost.Abstractions.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components.Pages;

public partial class SetupPage
{
    [Inject] private IUsersService UsersService { get; set; } = default!;
    [Inject] private IUserGroupsService UserGroupsService { get; set; } = default!;
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private int _currentStep = 0;
    private int _renderedStep = -1;

    private int CurrentStepProgress => ((_currentStep + 1) * 100) / 3;

    protected override async Task OnInitializedAsync()
    {
        var adminExists = await UsersService.HasAdminUserAsync();
        if (adminExists)
            _currentStep = 1;

        var venueExists = await VenuesService.HasAnyAsync();
        if (adminExists && venueExists)
            _currentStep = 2;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender && _renderedStep != _currentStep)
        {
            _renderedStep = _currentStep;
            await JS.InvokeVoidAsync("onPageOpen");
        }
    }

    private async Task MoveToNextStepAsync()
    {
        if (_currentStep < 2)
        {
            _currentStep++;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void OnSetupCompleteAsync()
    {
        NavigationManager.NavigateTo("/");
    }
}
