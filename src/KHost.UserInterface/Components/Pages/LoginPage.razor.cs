using KHost.Abstractions.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages;

public partial class LoginPage
{
    [Inject] private ICacheService? CacheService { get; set; }

    private string? _lastUser;
    private ElementReference _usernameInput;
    private ElementReference _passwordInput;

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

    // The page renders interactively without prerender, so the form enters the DOM after page
    // load and the browser ignores the autofocus attribute — focus has to be programmatic.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await (_lastUser is null ? _usernameInput : _passwordInput).FocusAsync();
    }
}
