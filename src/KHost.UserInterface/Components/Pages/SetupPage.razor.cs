using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components.Pages;

public partial class SetupPage
{
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    internal enum SetupStep { Backgrounds, Placeholder, Venue, FFmpeg, Media }

    // The list, not a count: whether the venue steps appear depends on an earlier run, so every
    // other step finds its place by membership rather than by a hardcoded number.
    private List<SetupStep> _steps = [SetupStep.Backgrounds, SetupStep.Placeholder, SetupStep.Venue, SetupStep.FFmpeg, SetupStep.Media];

    private int _currentStep;
    private int _renderedStep = -1;
    private bool _venueExists;

    private int CurrentStepProgress => ((_currentStep + 1) * 100) / _steps.Count;

    protected override async Task OnInitializedAsync()
    {
        _venueExists = await VenuesService.HasAnyAsync();

        BuildSteps();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender && _renderedStep != _currentStep)
        {
            _renderedStep = _currentStep;
            await JS.InvokeVoidAsync("onPageOpen");
        }
    }

    // A venue left by an earlier run drops its step: re-running it would create a second venue of
    // the same name. Backgrounds and Placeholder go with it: those choices exist to start that
    // first venue. FFmpeg stays: it proves nothing on disk, so a resumed setup checks again.
    private void BuildSteps()
    {
        _steps = [];
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
        // A full load, not a circuit navigation: the setup redirect is decided per HTTP request,
        // so only a fresh request lands on the console.
        NavigationManager.NavigateTo("/", forceLoad: true);
    }
}
