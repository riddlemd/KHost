using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components;

public partial class SettingsButton : IDisposable
{
    [Inject] private NavigationManager? NavigationManager { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }

    private readonly Stack<string> _history = new();
    private string _currentPath = "/";
    private bool _isNavigatingBack;
    private bool _isOnMainPage;

    private string ButtonText => _isOnMainPage ? "Open Settings Menu" : "Return";
    private string ButtonIcon => _isOnMainPage ? "bi-gear-fill" : "bi-arrow-return-left";

    protected override void OnInitialized()
    {
        _currentPath = new Uri(NavigationManager!.Uri).AbsolutePath;
        UpdateIsOnMainPage();
        NavigationManager.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (!_isNavigatingBack)
            _history.Push(_currentPath);
        else
            _isNavigatingBack = false;

        _currentPath = new Uri(e.Location).AbsolutePath;
        UpdateIsOnMainPage();
        InvokeAsync(StateHasChanged);
    }

    private void UpdateIsOnMainPage()
    {
        _isOnMainPage = new Uri(NavigationManager?.Uri ?? "/").AbsolutePath == "/";
    }

    private async Task OpenSettingsAsync()
    {
        if (!_isOnMainPage)
        {
            _isNavigatingBack = true;
            NavigationManager!.NavigateTo(_history.Count > 0 ? _history.Pop() : "/");
            return;
        }

        if (DialogService is not null)
            await DialogService.ShowSettingsMenuAsync(async () => { });
    }

    public void Dispose()
    {
        NavigationManager!.LocationChanged -= OnLocationChanged;
    }
}
