using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class AboutPage
{
    [Inject] private IAppInfoService AppInfo { get; set; } = default!;
    [Inject] private IExternalLinkService ExternalLinks { get; set; } = default!;

    private bool _licenseExpanded;
    private bool _noticesExpanded;

    private void OpenLink(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        ExternalLinks.Open(url);
    }
}
