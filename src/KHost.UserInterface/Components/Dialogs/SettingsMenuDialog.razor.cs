using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Hosting;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components.Dialogs;

public partial class SettingsMenuDialog
{
    [Inject] private NavigationManager? Navigation { get; set; }
    [Inject] private IHostApplicationLifetime? AppLifetime { get; set; }
    [Inject] private IJSRuntime? JS { get; set; }
    [Inject] private ISingerQueueService? SingerQueueService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private class SettingsPage
    {
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Route { get; set; } = "";
    }

    private readonly List<SettingsPage> _settingsPages =
    [
        new SettingsPage { Title = "Users Manager", Icon = "person-fill", Route = "/settings/users-manager" },
        new SettingsPage { Title = "User Groups Manager", Icon = "people-fill", Route = "/settings/user-groups-manager" },
        new SettingsPage { Title = "Venues Manager", Icon = "geo-alt-fill", Route = "/settings/venues-manager" },
        new SettingsPage { Title = "Tips Manager", Icon = "coin", Route = "/settings/tips-manager" },
        new SettingsPage { Title = "Media Manager", Icon = "music-note-list", Route = "/settings/media-manager" },
        new SettingsPage { Title = "Plugins Manager", Icon = "plug-fill", Route = "/settings/plugins-manager" }
    ];

    private async Task ConfirmExitAsync()
    {
        if (DialogService is null) return;

        await DialogService.ShowConfirmationAsync(
            "Are you sure you want to exit KHost?",
            () => _ = ExitAsync(),
            confirmText: "Exit"
        );
    }

    private async Task ExitAsync()
    {
        if (SingerQueueService is not null)
            await SingerQueueService.ClearAsync();

        if (JS is not null)
            await JS.InvokeVoidAsync("window.close");

        AppLifetime?.StopApplication();
    }

    private async Task NavigateToPageAsync(string route)
    {
        await OnClose.InvokeAsync();

        Navigation?.NavigateTo(route);
    }

    public record DialogRequest : BaseDialogRequest
    {
        public DialogRequest(Action? onClose) : base(onClose) { }
    }
}
