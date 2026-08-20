using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components.Pages;

public partial class SetupPage
{
    [Inject] private IUsersService UsersService { get; set; } = default!;
    [Inject] private IUserGroupsService UserGroupsService { get; set; } = default!;
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private IAppSettingsService AppSettings { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    internal enum SetupStep { Security, Admin, Venue, Media }

    // The list, not a count: whether Admin appears at all depends on the security choice, so
    // every other step finds its place by membership rather than by a hardcoded number.
    private List<SetupStep> _steps = [SetupStep.Security, SetupStep.Admin, SetupStep.Venue, SetupStep.Media];

    private int _currentStep;
    private int _renderedStep = -1;

    private int CurrentStepProgress => ((_currentStep + 1) * 100) / _steps.Count;

    protected override async Task OnInitializedAsync()
    {
        BuildSteps(AppSettings.Current.RequireLogin);

        // Resume where a half-finished setup left off. An overlay saying login is off proves
        // the security step ran; an existing admin proves the credential step ran.
        var requireLogin = AppSettings.Current.RequireLogin;
        var adminExists = await UsersService.HasAdminUserAsync();
        var venueExists = await VenuesService.HasAnyAsync();

        if ((!requireLogin || adminExists) && venueExists)
            _currentStep = _steps.IndexOf(SetupStep.Media);
        else if (!requireLogin || adminExists)
            _currentStep = _steps.IndexOf(SetupStep.Venue);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender && _renderedStep != _currentStep)
        {
            _renderedStep = _currentStep;
            await JS.InvokeVoidAsync("onPageOpen");
        }
    }

    private void BuildSteps(bool requireLogin)
        => _steps = requireLogin
            ? [SetupStep.Security, SetupStep.Admin, SetupStep.Venue, SetupStep.Media]
            : [SetupStep.Security, SetupStep.Venue, SetupStep.Media];

    private async Task OnSecurityCompletedAsync(bool requireLogin)
    {
        var current = AppSettings.Current;
        current.RequireLogin = requireLogin;
        await AppSettings.SaveAsync(current);

        BuildSteps(requireLogin);
        await MoveToNextStepAsync();
    }

    private async Task MoveToNextStepAsync()
    {
        if (_currentStep < _steps.Count - 1)
        {
            _currentStep++;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void OnSetupCompleteAsync()
    {
        // A full load, not a circuit navigation: the security choice takes effect per HTTP
        // request, and this circuit still carries the pre-setup anonymous identity.
        NavigationManager.NavigateTo("/", forceLoad: true);
    }
}
