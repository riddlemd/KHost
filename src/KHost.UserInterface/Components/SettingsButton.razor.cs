using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class SettingsButton
{
    private const string HomeRoute = "/";
    private const string ManageGroup = "Manage";
    private const string ApplicationGroup = "Application";

    [Inject] private NavigationManager? NavigationManager { get; set; }
    [Inject] private IPermissionService? Permissions { get; set; }
    [Inject] private IAppSettingsService? AppSettings { get; set; }

    private sealed class SettingsPage
    {
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Route { get; set; } = "";
        public string Group { get; set; } = ManageGroup;

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
        new SettingsPage { Title = "Media Manager", Icon = "music-note-list", Route = "/settings/media-manager", Requires = KHostPermission.ManageMedia },
        new SettingsPage { Title = "Plugins Manager", Icon = "plug-fill", Route = "/settings/plugins-manager", AdminOnly = true },
        new SettingsPage { Title = "App Settings", Icon = "gear-fill", Route = "/settings/app-settings", Group = ApplicationGroup, AdminOnly = true }
    ];

    private bool _canLock;

    private List<IGrouping<string, SettingsPage>> _groups = [];

    protected override async Task OnInitializedAsync()
    {
        // Locking a console that signs everyone in automatically would be a button to nowhere.
        _canLock = AppSettings?.Current.RequireLogin != false;

        _groups = [.. (await VisiblePagesAsync()).GroupBy(page => page.Group)];
    }

    private async Task<List<SettingsPage>> VisiblePagesAsync()
    {
        if (Permissions is null) return _allPages;

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

        return visible;
    }

    private void NavigateTo(string route) => NavigationManager?.NavigateTo(route);
}
