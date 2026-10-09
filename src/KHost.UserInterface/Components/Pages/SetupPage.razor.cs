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

    internal enum SetupStep { Admin, Backgrounds, Placeholder, Venue, FFmpeg, Media }

    // The list, not a count: whether Admin appears at all depends on Auth:RequireLogin, so every
    // other step finds its place by membership rather than by a hardcoded number.
    private List<SetupStep> _steps = [SetupStep.Admin, SetupStep.Backgrounds, SetupStep.Placeholder, SetupStep.Venue, SetupStep.FFmpeg, SetupStep.Media];

    private int _currentStep;
    private int _renderedStep = -1;
    private bool _venueExists;

    private int CurrentStepProgress => ((_currentStep + 1) * 100) / _steps.Count;

    protected override async Task OnInitializedAsync()
    {
        // Sign-in is a config flag now, not a wizard choice: BuildSteps reads what the host
        // already set in appsettings.json rather than asking.
        var requireLogin = AppSettings.Current.RequireLogin;

        // Resume where a half-finished setup left off. HasAdminWithPasswordAsync, not
        // HasAdminUserAsync: an admin row with no password has not actually cleared this step,
        // and must not be read as having done so.
        var adminExists = await UsersService.HasAdminWithPasswordAsync();
        _venueExists = await VenuesService.HasAnyAsync();

        BuildSteps(requireLogin);

        // FFmpeg rather than Media: it proves nothing on disk, so a resumed setup checks again,
        // and a machine that has it just moves on.
        if (!requireLogin || adminExists)
            _currentStep = _steps.IndexOf(_venueExists ? SetupStep.FFmpeg : SetupStep.Backgrounds);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender && _renderedStep != _currentStep)
        {
            _renderedStep = _currentStep;
            await JS.InvokeVoidAsync("onPageOpen");
        }
    }

    // A venue left by an earlier run drops its step: setup restarts at Admin whenever no admin
    // has a password, and re-running the venue step would create a second venue of the same name.
    // Backgrounds and Placeholder go with it: those choices exist to start that first venue.
    private void BuildSteps(bool requireLogin)
    {
        _steps = [];
        if (requireLogin) _steps.Add(SetupStep.Admin);
        if (!_venueExists)
        {
            _steps.Add(SetupStep.Backgrounds);
            _steps.Add(SetupStep.Placeholder);
            _steps.Add(SetupStep.Venue);
        }
        _steps.Add(SetupStep.FFmpeg);
        _steps.Add(SetupStep.Media);
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
        // A full load, not a circuit navigation: whether a request is signed in as the console
        // admin is decided per HTTP request, and this circuit still carries the pre-setup
        // anonymous identity.
        NavigationManager.NavigateTo("/", forceLoad: true);
    }
}
