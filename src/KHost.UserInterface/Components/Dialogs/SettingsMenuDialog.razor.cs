using KHost.Abstractions.Models;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class SettingsMenuDialog
{
    [Inject] private NavigationManager? Navigation { get; set; }
    [Inject] private IPermissionService? Permissions { get; set; }

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private class SettingsPage
    {
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Route { get; set; } = "";

        /// <summary>Null means any signed-in user; the page itself enforces the same rule.</summary>
        public KHostPermission? Requires { get; set; }

        public bool AdminOnly { get; set; }
    }

    private static readonly List<SettingsPage> _allPages =
    [
        new SettingsPage { Title = "Users Manager", Icon = "person-fill", Route = "/settings/users-manager", Requires = KHostPermission.EditUser },
        new SettingsPage { Title = "User Groups Manager", Icon = "people-fill", Route = "/settings/user-groups-manager", Requires = KHostPermission.EditGroup },
        new SettingsPage { Title = "Venues Manager", Icon = "geo-alt-fill", Route = "/settings/venues-manager", Requires = KHostPermission.EditVenue },
        new SettingsPage { Title = "Tips Manager", Icon = "coin", Route = "/settings/tips-manager" },
        new SettingsPage { Title = "Media Manager", Icon = "music-note-list", Route = "/settings/media-manager" },
        new SettingsPage { Title = "Plugins Manager", Icon = "plug-fill", Route = "/settings/plugins-manager", AdminOnly = true }
    ];

    private List<SettingsPage> _settingsPages = [];

    protected override async Task OnInitializedAsync()
    {
        if (Permissions is null)
        {
            _settingsPages = _allPages;
            return;
        }

        var visible = new List<SettingsPage>();
        var isAdmin = await Permissions.IsAdminAsync();

        foreach (var page in _allPages)
        {
            var allowed = page switch
            {
                { AdminOnly: true } => isAdmin,
                { Requires: { } permission } => await Permissions.HasAsync(permission),
                _ => true,
            };

            if (allowed) visible.Add(page);
        }

        _settingsPages = visible;
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
