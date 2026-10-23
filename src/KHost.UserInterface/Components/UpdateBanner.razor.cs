using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

/// <summary>Offers a downloaded update; renders nothing without one.</summary>
public partial class UpdateBanner : IDisposable
{
    // Optional: only the window registers the update service.
    [Inject] private IServiceProvider Services { get; set; } = default!;

    private AppUpdateService? Updates { get; set; }

    protected override void OnInitialized()
    {
        Updates = Services.GetService<AppUpdateService>();
        if (Updates is not null) Updates.Changed += OnChanged;
    }

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);

    private void Restart() => Updates?.RestartToUpdate();

    public void Dispose()
    {
        if (Updates is not null) Updates.Changed -= OnChanged;
    }
}
