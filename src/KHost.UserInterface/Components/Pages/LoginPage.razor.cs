using KHost.Abstractions.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages;

public partial class LoginPage
{
    [Inject] private ICacheService? CacheService { get; set; }

    private string? _lastUser;

    /// <summary>
    /// Set by the login endpoint's redirect when the credentials were wrong. A string, not a
    /// bool: the query binder throws on anything but true/false, and a crashed circuit renders
    /// a white window instead of an error banner.
    /// </summary>
    [SupplyParameterFromQuery(Name = "failed")]
    public string? Failed { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (CacheService is not null)
            _lastUser = await CacheService.LoadAsync<string>(Program.LastLoginCacheKey);
    }
}
