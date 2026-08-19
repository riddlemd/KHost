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

    // Naming the steps keeps the progress bar, the label, the clamp and the resume points from
    // drifting apart; adding one means a constant here and a branch in the render chain.
    private const int AdminStep = 0;
    private const int VenueStep = 1;
    private const int MediaStep = 2;
    private const int StepCount = 3;

    private int _currentStep = AdminStep;
    private int _renderedStep = -1;

    private int CurrentStepProgress => ((_currentStep + 1) * 100) / StepCount;

    protected override async Task OnInitializedAsync()
    {
        var adminExists = await UsersService.HasAdminUserAsync();
        if (adminExists)
            _currentStep = VenueStep;

        var venueExists = await VenuesService.HasAnyAsync();
        if (adminExists && venueExists)
            _currentStep = MediaStep;
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
        if (_currentStep < StepCount - 1)
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
