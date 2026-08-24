using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class AboutPage
{
    [Inject] private IAppInfoService? AppInfo { get; set; }
    [Inject] private IExternalLinkService? ExternalLinks { get; set; }

    private IAppInfoService? _appInfo;
    private bool _licenseExpanded;
    private bool _noticesExpanded;

    protected override void OnInitialized() => _appInfo = AppInfo;

    private void OpenLink(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        ExternalLinks?.Open(url);
    }
}
