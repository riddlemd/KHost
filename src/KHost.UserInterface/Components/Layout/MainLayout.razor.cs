using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components.Layout;

public partial class MainLayout : IDisposable
{
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private bool _pendingPageOpen;

    // The console owns the viewport and never scrolls; every other page scrolls as a document,
    // which takes the status bar with it.
    private bool IsMainPage =>
        string.IsNullOrEmpty(NavigationManager.ToBaseRelativePath(NavigationManager.Uri).Split('?', '#')[0].Trim('/'));

    protected override void OnInitialized()
    {
        NavigationManager.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        _pendingPageOpen = true;
        InvokeAsync(StateHasChanged);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender || _pendingPageOpen)
        {
            _pendingPageOpen = false;
            await JS.InvokeVoidAsync("onPageOpen");
        }
    }

    public void Dispose()
    {
        NavigationManager.LocationChanged -= OnLocationChanged;
    }
}
